using System.Text.Json;

namespace Recta.App.NativeInterop;

// 高层封装:负返回码统一转 RectaException(携带原生层人类可读消息)。
// 前端 GUI 只与本类打交道,不直接触碰 P/Invoke 与 JSON。
public sealed class RectaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string Version()
    {
        return CallString((buf, cap) => RectaNative.recta_version(buf, cap));
    }

    /// <param name="connectionString">null 时原生侧按 DATABASE_URL / .env.local 装载。</param>
    public void Init(string? connectionString = null)
    {
        Check(RectaNative.recta_init(connectionString));
    }

    /// <summary>连接 recta-test 测试分支(集成测试用)。</summary>
    public void InitTest()
    {
        Check(RectaNative.recta_init_test());
    }

    public void Shutdown()
    {
        RectaNative.recta_shutdown();
    }

    // ---- 领域纯函数 ----

    public static string FormatMoney(long cents)
    {
        return CallString((buf, cap) => RectaNative.recta_money_format(cents, buf, cap));
    }

    public static long ParseMoney(string text)
    {
        var rc = RectaNative.recta_money_parse(text, out var cents);
        return rc >= 0 ? cents : throw ToException(rc);
    }

    public static DistributeResult Distribute(long totalCents, IReadOnlyList<string> studentIds,
                                              string tailBearerId)
    {
        var idsJson = JsonSerializer.Serialize(studentIds);
        var json = CallString((buf, cap) =>
            RectaNative.recta_distribute(totalCents, idsJson, tailBearerId, buf, cap));
        return Deserialize<DistributeResult>(json);
    }

    // ---- 认证与账号管理 ----

    public LoginSession Login(string username, string password)
    {
        var json = CallString((buf, cap) => RectaNative.recta_login(username, password, buf, cap));
        return Deserialize<LoginSession>(json);
    }

    public void ChangePassword(string userId, string oldPassword, string newPassword)
    {
        Check(RectaNative.recta_change_password(userId, oldPassword, newPassword));
    }

    /// <returns>一次性明文临时密码(仅此一次返回,由团支书线下转交)。</returns>
    public string CreateUser(string actorId, string newUserId, string username,
                             string displayName, string role)
    {
        return CallString((buf, cap) => RectaNative.recta_create_user(
            actorId, newUserId, username, displayName, role, buf, cap));
    }

    public string ResetPassword(string actorId, string targetUserId)
    {
        return CallString((buf, cap) =>
            RectaNative.recta_reset_password(actorId, targetUserId, buf, cap));
    }

    public void DeactivateUser(string actorId, string targetUserId)
    {
        Check(RectaNative.recta_deactivate_user(actorId, targetUserId));
    }

    public void ActivateUser(string actorId, string targetUserId)
    {
        Check(RectaNative.recta_activate_user(actorId, targetUserId));
    }

    public void UpdateDisplayName(string actorId, string targetUserId, string displayName)
    {
        Check(RectaNative.recta_update_display_name(actorId, targetUserId, displayName));
    }

    public string BootstrapSecretary(string username, string displayName)
    {
        return CallString((buf, cap) =>
            RectaNative.recta_bootstrap_secretary(username, displayName, buf, cap));
    }

    // ---- 工作流 ----

    public int SubmitRequest(string actorId, string title, string category, long appliedCents,
                             IReadOnlyList<string>? splitParticipantIds = null,
                             string? tailBearerId = null, string? voucherUrl = null)
    {
        var idsJson = splitParticipantIds is null ? null : JsonSerializer.Serialize(splitParticipantIds);
        var rc = RectaNative.recta_submit_request(actorId, title, category, appliedCents, idsJson,
                                                  tailBearerId, voucherUrl, out var requestId);
        return rc >= 0 ? requestId : throw ToException(rc);
    }

    public void ApproveRequest(string actorId, int requestId, long approvedCents, string? notes = null)
    {
        Check(RectaNative.recta_approve_request(actorId, requestId, approvedCents, notes));
    }

    public void RejectRequest(string actorId, int requestId, string rejectCategory, string notes)
    {
        Check(RectaNative.recta_reject_request(actorId, requestId, rejectCategory, notes));
    }

    public SettlementOutcome SettleRequest(string actorId, int requestId, string? extraNotes = null)
    {
        var json = CallString((buf, cap) =>
            RectaNative.recta_settle_request(actorId, requestId, extraNotes, buf, cap));
        return Deserialize<SettlementOutcome>(json);
    }

    public void RecordInflow(string actorId, string destination, long amountCents,
                             string sourceTitle, string? targetStudentId = null,
                             int? relatedRequestId = null, string? voucherUrl = null)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["destination"] = destination,
            ["amount_cents"] = amountCents,
            ["source_title"] = sourceTitle,
            ["target_student_id"] = targetStudentId,
            ["related_request_id"] = relatedRequestId,
            ["voucher_url"] = voucherUrl,
        });
        Check(RectaNative.recta_record_inflow(actorId, payload));
    }

    /// <summary>班费批量充值:单事务内为多名同学各充等额一笔(原子)。返回实际人数。</summary>
    public int RecordInflowBatch(string actorId, string sourceTitle, long amountCents,
                                 string? voucherUrl, IReadOnlyList<string> studentIds)
    {
        var idsJson = JsonSerializer.Serialize(studentIds);
        var rc = RectaNative.recta_record_inflow_batch(actorId, sourceTitle, amountCents,
                                                       voucherUrl, idsJson, out var count);
        return rc >= 0 ? count : throw ToException(rc);
    }

    public void AddStudent(string actorId, string studentId, string name)
    {
        Check(RectaNative.recta_add_student(actorId, studentId, name));
    }

    public void RenameStudent(string actorId, string studentId, string name)
    {
        Check(RectaNative.recta_rename_student(actorId, studentId, name));
    }

    // ---- 查询 ----

    public IReadOnlyList<ExpenseRequestDto> ListRequests(string? status = null,
                                                         string? category = null,
                                                         string? applicantId = null)
    {
        var json = CallString((buf, cap) =>
            RectaNative.recta_list_requests(status, category, applicantId, buf, cap));
        return Deserialize<RequestList>(json).Requests;
    }

    public RequestBundle GetRequest(int requestId)
    {
        var json = CallString((buf, cap) => RectaNative.recta_get_request(requestId, buf, cap));
        return Deserialize<RequestBundle>(json);
    }

    public IReadOnlyList<StudentDto> ListStudents()
    {
        var json = CallString((buf, cap) => RectaNative.recta_list_students(buf, cap));
        return Deserialize<StudentList>(json).Students;
    }

    public IReadOnlyList<EntityAccountDto> ListAccounts()
    {
        var json = CallString((buf, cap) => RectaNative.recta_list_accounts(buf, cap));
        return Deserialize<AccountList>(json).Accounts;
    }

    public IReadOnlyList<UserDto> ListUsers()
    {
        var json = CallString((buf, cap) => RectaNative.recta_list_users(buf, cap));
        return Deserialize<UserList>(json).Users;
    }

    public OverviewDto GetOverview()
    {
        var json = CallString((buf, cap) => RectaNative.recta_get_overview(buf, cap));
        return Deserialize<OverviewDto>(json);
    }

    public ChangeEventBatch FetchChangeEvents(long afterSeq, int limit = 100)
    {
        var json = CallString((buf, cap) =>
            RectaNative.recta_fetch_change_events(afterSeq, limit, buf, cap));
        return Deserialize<ChangeEventBatch>(json);
    }

    public IReadOnlyList<LedgerEntryDto> ListStudentLedger(string studentId, int limit = 100)
    {
        var json = CallString((buf, cap) =>
            RectaNative.recta_list_student_ledger(studentId, limit, buf, cap));
        return Deserialize<LedgerEntryList>(json).Entries;
    }

    public IReadOnlyList<InflowDto> ListInflows(int limit = 100)
    {
        var json = CallString((buf, cap) => RectaNative.recta_list_inflows(limit, buf, cap));
        return Deserialize<InflowList>(json).Inflows;
    }

    /// <summary>团支书专属全员统计(§5.4)。非团支书将收到 Permission 错误码。</summary>
    public AuditStatistics GetAuditStatistics(string actorId)
    {
        var json = CallString((buf, cap) => RectaNative.recta_get_audit_statistics(actorId, buf, cap));
        return Deserialize<AuditStatistics>(json);
    }

    public BudgetOverview GetBudgetOverview()
    {
        var json = CallString((buf, cap) => RectaNative.recta_get_budget_overview(buf, cap));
        return Deserialize<BudgetOverview>(json);
    }

    // ---- 增量同步 ----

    public void SyncStart()
    {
        Check(RectaNative.recta_sync_start());
    }

    public void SyncStop()
    {
        RectaNative.recta_sync_stop();
    }

    public SyncStatusDto SyncStatus()
    {
        var json = CallString((buf, cap) => RectaNative.recta_sync_status(buf, cap));
        return Deserialize<SyncStatusDto>(json);
    }

    public SyncDrainDto SyncDrain()
    {
        var json = CallString((buf, cap) => RectaNative.recta_sync_drain(buf, cap));
        return Deserialize<SyncDrainDto>(json);
    }

    // ---- 内部:返回码与缓冲 plumbing ----

    private static string CallString(Func<byte[], int, int> invoke)
    {
        var buffer = new byte[RectaNative.BufMax];
        var rc = invoke(buffer, buffer.Length);
        return rc >= 0
            ? System.Text.Encoding.UTF8.GetString(buffer, 0, rc)
            : throw ToException(rc);
    }

    private static void Check(int rc)
    {
        if (rc < 0)
        {
            throw ToException(rc);
        }
    }

    private static RectaException ToException(int rc)
    {
        var message = "(原生层未提供错误信息)";
        try
        {
            var buffer = new byte[4096];
            var len = RectaNative.recta_last_error(buffer, buffer.Length);
            if (len > 0)
            {
                message = System.Text.Encoding.UTF8.GetString(buffer, 0, len);
            }
        }
        catch
        {
            // 取错误信息本身失败时保留兜底文案。
        }
        return new RectaException(rc, message);
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new RectaException(RectaErrors.Unknown, "JSON 反序列化得到 null");
    }
}
