using System.Runtime.InteropServices;

namespace Recta.App.NativeInterop;

// recta_capi 的 P/Invoke 声明。一律走 UTF-8 字符串与 byte[] 输出缓冲,
// 金额一律 long(整数分)——C# 侧同样严禁浮点(Vibe.md §8.1)。
internal static partial class RectaNative
{
    private const string Lib = "recta_capi";

    public const int BufMax = 262144; // RECTA_BUF_MAX

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_init(string? connection_string);

    [LibraryImport(Lib)]
    internal static partial int recta_init_test();

    [LibraryImport(Lib)]
    internal static partial void recta_shutdown();

    [LibraryImport(Lib)]
    internal static partial int recta_version(byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_last_error(byte[] buf, int cap);

    // ---- 领域纯函数 ----
    [LibraryImport(Lib)]
    internal static partial int recta_money_format(long cents, byte[] buf, int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_money_parse(string text, out long out_cents);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_distribute(long total_cents, string ids_json,
                                                 string tail_bearer_id, byte[] buf, int cap);

    // ---- 认证与账号管理 ----
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_login(string username, string password, byte[] buf, int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_change_password(string user_id, string old_password,
                                                      string new_password);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_create_user(string actor_id, string new_user_id,
                                                  string username, string display_name,
                                                  string role_name, byte[] buf, int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_reset_password(string actor_id, string target_user_id,
                                                     byte[] buf, int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_deactivate_user(string actor_id, string target_user_id);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_update_display_name(string actor_id, string target_user_id,
                                                          string display_name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_bootstrap_secretary(string username, string display_name,
                                                          byte[] buf, int cap);

    // ---- 工作流 ----
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_submit_request(string actor_id, string title,
                                                     string category, long applied_cents,
                                                     string? split_ids_json,
                                                     string? tail_bearer_id,
                                                     out int out_request_id);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_approve_request(string actor_id, int request_id,
                                                      long approved_cents, string? notes);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_reject_request(string actor_id, int request_id, string notes);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_settle_request(string actor_id, int request_id,
                                                     string? extra_notes, byte[] buf, int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_record_inflow(string actor_id, string inflow_json);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_add_student(string actor_id, string student_id, string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_rename_student(string actor_id, string student_id,
                                                     string name);

    // ---- 查询 ----
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_list_requests(string? status_filter,
                                                    string? category_filter,
                                                    string? applicant_id_filter, byte[] buf,
                                                    int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_get_request(int request_id, byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_list_students(byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_list_accounts(byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_list_users(byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_get_overview(byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_fetch_change_events(long after_seq, int limit, byte[] buf,
                                                          int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_list_student_ledger(string student_id, int limit, byte[] buf,
                                                          int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_list_inflows(int limit, byte[] buf, int cap);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_get_audit_statistics(string actor_id, byte[] buf, int cap);

    [LibraryImport(Lib)]
    internal static partial int recta_get_budget_overview(byte[] buf, int cap);
}
