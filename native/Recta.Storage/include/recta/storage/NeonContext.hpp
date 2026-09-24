#pragma once
// Neon 抗休眠执行器(Vibe.md §7.2)。
// Neon Scale-to-Zero 休眠后首次连接表现为 broken_connection——
// 带退避的重试循环负责唤醒实例;每次尝试使用全新短连接(serverless 语义)。
#include <pqxx/pqxx>

#include <chrono>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>

namespace recta::storage {

class NeonContext {
private:
    std::string conn_str_;

public:
    explicit NeonContext(std::string conn_str) : conn_str_(std::move(conn_str)) {}

    // 同步监听连接需要非池化端点(PgBouncer 事务模式不支持 LISTEN/NOTIFY)。
    [[nodiscard]] const std::string& connection_string() const noexcept { return conn_str_; }

    template <typename TxFunc>
    auto ExecuteTransaction(TxFunc&& func, int max_retries = 3) {
        for (int attempt = 1; attempt <= max_retries; ++attempt) {
            try {
                pqxx::connection conn(conn_str_);
                pqxx::work tx(conn);
                auto result = func(tx);
                tx.commit();
                return result;
            } catch (const pqxx::broken_connection&) {
                if (attempt == max_retries) throw;
                std::cout << "[Neon] 实例冷启动唤醒中，等待重试 (" << attempt << "/" << max_retries << ")...\n";
                std::this_thread::sleep_for(std::chrono::milliseconds(600 * attempt));
            }
        }
        throw std::runtime_error("Neon 连接重试耗尽");
    }
};

} // namespace recta::storage
