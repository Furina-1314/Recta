#pragma once
// Neon 抗休眠执行器(Vibe.md §7.2,2026-09-25 性能重构)。
// 连接复用:桌面应用按实例缓存一条连接,免除每次事务的 TCP+TLS 握手
// (跨城 Neon 握手数百毫秒,是界面卡顿的主因);mutex 串行化——桌面单用户足够。
// 断线处理:broken_connection → 重建连接 + 带退避重试;其余异常 → 丢弃连接
// (事务可能处于 aborted 状态)并原样抛出,下次调用获得全新连接。
#include <pqxx/pqxx>

#include <chrono>
#include <iostream>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>

namespace recta::storage {

class NeonContext {
private:
    std::string conn_str_;
    mutable std::mutex mutex_;
    std::unique_ptr<pqxx::connection> conn_;

    // 调用方必须已持有 mutex_。
    void EnsureConnectionLocked() {
        if (conn_ && conn_->is_open()) return;
        conn_.reset();
        for (int attempt = 1; attempt <= 3; ++attempt) {
            try {
                conn_ = std::make_unique<pqxx::connection>(conn_str_);
                return;
            } catch (const pqxx::broken_connection&) {
                if (attempt == 3) throw;
                std::cout << "[Neon] 建连失败，等待重试 (" << attempt << "/3)...\n";
                std::this_thread::sleep_for(std::chrono::milliseconds(600 * attempt));
            }
        }
    }

    void DropConnection() noexcept {
        try {
            if (conn_) conn_->close();
        } catch (...) {
        }
        conn_.reset();
    }

public:
    explicit NeonContext(std::string conn_str) : conn_str_(std::move(conn_str)) {}

    // 同步监听连接需要非池化端点(PgBouncer 事务模式不支持 LISTEN/NOTIFY)。
    [[nodiscard]] const std::string& connection_string() const noexcept { return conn_str_; }

    template <typename TxFunc>
    auto ExecuteTransaction(TxFunc&& func, int max_retries = 3) {
        std::lock_guard<std::mutex> lock(mutex_);
        for (int attempt = 1; attempt <= max_retries; ++attempt) {
            try {
                EnsureConnectionLocked();
                pqxx::work tx(*conn_);
                auto result = func(tx);
                tx.commit();
                return result;
            } catch (const pqxx::broken_connection&) {
                DropConnection();
                if (attempt == max_retries) throw;
                std::cout << "[Neon] 连接断开，等待重试 (" << attempt << "/" << max_retries << ")...\n";
                std::this_thread::sleep_for(std::chrono::milliseconds(600 * attempt));
            } catch (...) {
                // 事务可能处于 aborted 状态:丢弃连接,保证下次调用拿到干净连接。
                DropConnection();
                throw;
            }
        }
        throw std::runtime_error("Neon 连接重试耗尽");
    }
};

} // namespace recta::storage
