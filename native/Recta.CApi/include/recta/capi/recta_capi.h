// Recta C ABI —— 原生核心(recta_capi.dll / librecta_capi.so)对 Avalonia 前端的导出面。
//
// 返回值约定:
//   · 带 buf 的函数:成功返回写入的字符数(不含结尾 NUL,总在 cap>0 时写入 NUL);
//     buf 容量不足返回 RECTA_ERR_BUFFER_SMALL,调用方扩容后重试(建议预分配 RECTA_BUF_MAX)。
//   · 不带 buf 的函数:成功返回 0;指针型出参用于返回数值。
//   · 一切失败为负错误码,人类可读详情经 recta_last_error 获取(线程局部)。
//   · 异常绝不跨越本边界;金额一律整数分(int64),字符串一律 UTF-8,复杂结构一律 JSON。
#ifndef RECTA_CAPI_H
#define RECTA_CAPI_H

#if defined(_WIN32)
#if defined(RECTA_API_EXPORTS)
#define RECTA_API __declspec(dllexport)
#else
#define RECTA_API __declspec(dllimport)
#endif
#else
#define RECTA_API __attribute__((visibility("default")))
#endif

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define RECTA_ERR_INVALID_ARG (-1)
#define RECTA_ERR_PERMISSION (-2)
#define RECTA_ERR_AUTH (-3)
#define RECTA_ERR_STATE (-4)
#define RECTA_ERR_WEAK_PASSWORD (-5)
#define RECTA_ERR_DB (-6)
#define RECTA_ERR_LOGIC (-7)
#define RECTA_ERR_NOT_READY (-8)
#define RECTA_ERR_BUFFER_SMALL (-9)
#define RECTA_ERR_UNKNOWN (-10)

/* 输出缓冲建议上限(256 KiB),足够容纳单页台账/名单 JSON。 */
#define RECTA_BUF_MAX 262144

/* ---- 生命周期 ---- */
/* connection_string 为 NULL 时按 DATABASE_URL / .env.local 装载。重复 init 会先关闭旧上下文。 */
RECTA_API int32_t recta_init(const char* connection_string);
/* 连接 recta-test 测试分支(RECTA_TEST_DATABASE_URL / .env.test.local);找不到来源返回 RECTA_ERR_DB。 */
RECTA_API int32_t recta_init_test(void);
RECTA_API void recta_shutdown(void);
RECTA_API int32_t recta_version(char* buf, int32_t cap);
RECTA_API int32_t recta_last_error(char* buf, int32_t cap);

/* ---- 领域纯函数(无数据库,GUI 即时预览/校验用) ---- */
RECTA_API int32_t recta_money_format(int64_t cents, char* buf, int32_t cap);
RECTA_API int32_t recta_money_parse(const char* text, int64_t* out_cents);
/* ids_json: ["s1","s2",...];出参 {"allocations":[...],"count":n,"sum_cents":M} */
RECTA_API int32_t recta_distribute(int64_t total_cents, const char* ids_json,
                                   const char* tail_bearer_id, char* buf, int32_t cap);

/* ---- 认证与账号管理 ---- */
RECTA_API int32_t recta_login(const char* username, const char* password, char* buf, int32_t cap);
RECTA_API int32_t recta_change_password(const char* user_id, const char* old_password,
                                        const char* new_password);
RECTA_API int32_t recta_create_user(const char* actor_id, const char* new_user_id,
                                    const char* username, const char* display_name,
                                    const char* role_name, char* buf, int32_t cap /*临时口令*/);
RECTA_API int32_t recta_reset_password(const char* actor_id, const char* target_user_id,
                                       char* buf, int32_t cap /*临时口令*/);
RECTA_API int32_t recta_deactivate_user(const char* actor_id, const char* target_user_id);
RECTA_API int32_t recta_update_display_name(const char* actor_id, const char* target_user_id,
                                            const char* display_name);
RECTA_API int32_t recta_bootstrap_secretary(const char* username, const char* display_name,
                                            char* buf, int32_t cap /*临时口令*/);

/* ---- 工作流 ---- */
/* category: "FLEXIBLE" | "FACULTY" | "CLASS_FUND";split_ids_json/tail_bearer_id 班费必填,其余渠道传 NULL。 */
RECTA_API int32_t recta_submit_request(const char* actor_id, const char* title,
                                       const char* category, int64_t applied_cents,
                                       const char* split_ids_json, const char* tail_bearer_id,
                                       int32_t* out_request_id);
RECTA_API int32_t recta_approve_request(const char* actor_id, int32_t request_id,
                                        int64_t approved_cents, const char* notes /*可空*/);
RECTA_API int32_t recta_reject_request(const char* actor_id, int32_t request_id,
                                       const char* notes);
RECTA_API int32_t recta_settle_request(const char* actor_id, int32_t request_id,
                                       const char* extra_notes /*可空*/, char* buf, int32_t cap);
/* inflow_json: {"destination":"TO_...","amount_cents":N,"source_title":"...",
                 "target_student_id":".."?,"related_request_id":N?,"voucher_url":".."?} */
RECTA_API int32_t recta_record_inflow(const char* actor_id, const char* inflow_json);
RECTA_API int32_t recta_add_student(const char* actor_id, const char* student_id,
                                    const char* name);
RECTA_API int32_t recta_rename_student(const char* actor_id, const char* student_id,
                                       const char* name);

/* ---- 查询(只读,GUI 拉数据) ---- */
/* status_filter/category_filter/applicant_id_filter 均可传 NULL。 */
RECTA_API int32_t recta_list_requests(const char* status_filter, const char* category_filter,
                                      const char* applicant_id_filter, char* buf, int32_t cap);
RECTA_API int32_t recta_get_request(int32_t request_id, char* buf, int32_t cap /*含 splits*/);
RECTA_API int32_t recta_list_students(char* buf, int32_t cap);
RECTA_API int32_t recta_list_accounts(char* buf, int32_t cap);
RECTA_API int32_t recta_list_users(char* buf, int32_t cap /*不含口令哈希*/);
/* 总览:守恒三元组 + 各状态单量 + 两实体账户余额。 */
RECTA_API int32_t recta_get_overview(char* buf, int32_t cap);
RECTA_API int32_t recta_fetch_change_events(int64_t after_seq, int32_t limit, char* buf,
                                            int32_t cap);

/* ---- 开发/测试专用(编译期 RECTA_DEV_TOOLS 门控;发布构建必须关闭该选项) ---- */
#ifdef RECTA_DEV_TOOLS
/* 清空全部业务表(测试分支复位)。 */
RECTA_API int32_t recta_dev_truncate_all(void);
#endif

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* RECTA_CAPI_H */
