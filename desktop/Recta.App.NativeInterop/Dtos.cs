using System.Text.Json.Serialization;

namespace Recta.App.NativeInterop;

// 与 recta_capi JSON 载荷一一对应的 DTO(蛇形命名与原生侧对齐)。
public sealed record LoginSession(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword);

public sealed record AllocationLine(
    [property: JsonPropertyName("student_id")] string StudentId,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("is_tail_bearer")] bool IsTailBearer);

public sealed record DistributeResult(
    [property: JsonPropertyName("allocations")] IReadOnlyList<AllocationLine> Allocations,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("sum_cents")] long SumCents);

public sealed record ExpenseRequestDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("account_category")] string AccountCategory,
    [property: JsonPropertyName("applied_amount_cents")] long AppliedAmountCents,
    [property: JsonPropertyName("approved_amount_cents")] long? ApprovedAmountCents,
    [property: JsonPropertyName("settled_amount_cents")] long? SettledAmountCents,
    [property: JsonPropertyName("applicant_id")] string ApplicantId,
    [property: JsonPropertyName("reviewer_id")] string? ReviewerId,
    [property: JsonPropertyName("settler_id")] string? SettlerId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("review_notes")] string? ReviewNotes,
    [property: JsonPropertyName("settlement_notes")] string? SettlementNotes,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("reviewed_at")] string? ReviewedAt,
    [property: JsonPropertyName("settled_at")] string? SettledAt);

public sealed record SplitDto(
    [property: JsonPropertyName("student_id")] string StudentId,
    [property: JsonPropertyName("student_name")] string? StudentName,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("is_tail_bearer")] bool IsTailBearer,
    [property: JsonPropertyName("advance_cents")] long AdvanceCents);

public sealed record RequestBundle(
    [property: JsonPropertyName("request")] ExpenseRequestDto Request,
    [property: JsonPropertyName("splits")] IReadOnlyList<SplitDto> Splits);

public sealed record RequestList(
    [property: JsonPropertyName("requests")] IReadOnlyList<ExpenseRequestDto> Requests);

public sealed record StudentDto(
    [property: JsonPropertyName("student_id")] string StudentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("balance_cents")] long BalanceCents,
    [property: JsonPropertyName("total_recharged_cents")] long TotalRechargedCents,
    [property: JsonPropertyName("total_spent_cents")] long TotalSpentCents);

public sealed record StudentList(
    [property: JsonPropertyName("students")] IReadOnlyList<StudentDto> Students);

public sealed record EntityAccountDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("balance_cents")] long BalanceCents,
    [property: JsonPropertyName("custodian_id")] string? CustodianId);

public sealed record AccountList(
    [property: JsonPropertyName("accounts")] IReadOnlyList<EntityAccountDto> Accounts);

public sealed record UserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("last_login_at")] string? LastLoginAt);

public sealed record UserList(
    [property: JsonPropertyName("users")] IReadOnlyList<UserDto> Users);

public sealed record CustodySnapshot(
    [property: JsonPropertyName("balances_sum_cents")] long BalancesSumCents,
    [property: JsonPropertyName("custodian_cash_cents")] long CustodianCashCents,
    [property: JsonPropertyName("advance_total_cents")] long AdvanceTotalCents);

public sealed record AdvanceLine(
    [property: JsonPropertyName("student_id")] string StudentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("amount_cents")] long AmountCents);

public sealed record SettlementOutcome(
    [property: JsonPropertyName("request_id")] int RequestId,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("settled_cents")] long SettledCents,
    [property: JsonPropertyName("advances")] IReadOnlyList<AdvanceLine> Advances,
    [property: JsonPropertyName("total_advance_cents")] long TotalAdvanceCents,
    [property: JsonPropertyName("custody")] CustodySnapshot Custody);

public sealed record OverviewDto(
    [property: JsonPropertyName("custody")] CustodyWithFlag Custody,
    [property: JsonPropertyName("status_counts")] Dictionary<string, long> StatusCounts,
    [property: JsonPropertyName("flexible_balance_cents")] long FlexibleBalanceCents,
    [property: JsonPropertyName("faculty_hanging_cents")] long FacultyHangingCents);

public sealed record CustodyWithFlag(
    [property: JsonPropertyName("balances_sum_cents")] long BalancesSumCents,
    [property: JsonPropertyName("custodian_cash_cents")] long CustodianCashCents,
    [property: JsonPropertyName("advance_total_cents")] long AdvanceTotalCents,
    [property: JsonPropertyName("conserved")] bool Conserved);

public sealed record ChangeEventDto(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("payload")] string Payload);

public sealed record ChangeEventBatch(
    [property: JsonPropertyName("events")] IReadOnlyList<ChangeEventDto> Events,
    [property: JsonPropertyName("max_seq")] long MaxSeq);

public sealed record LedgerEntryDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("expense_request_id")] int? ExpenseRequestId,
    [property: JsonPropertyName("inflow_record_id")] long? InflowRecordId,
    [property: JsonPropertyName("entry_type")] string EntryType,
    [property: JsonPropertyName("change_cents")] long ChangeCents,
    [property: JsonPropertyName("balance_after_cents")] long BalanceAfterCents,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("created_at")] string? CreatedAt);

public sealed record LedgerEntryList(
    [property: JsonPropertyName("entries")] IReadOnlyList<LedgerEntryDto> Entries);

public sealed record InflowDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("source_title")] string SourceTitle,
    [property: JsonPropertyName("destination_type")] string DestinationType,
    [property: JsonPropertyName("target_student_id")] string? TargetStudentId,
    [property: JsonPropertyName("related_request_id")] int? RelatedRequestId,
    [property: JsonPropertyName("operator_id")] string OperatorId,
    [property: JsonPropertyName("voucher_file_url")] string? VoucherFileUrl,
    [property: JsonPropertyName("created_at")] string? CreatedAt);

public sealed record InflowList(
    [property: JsonPropertyName("inflows")] IReadOnlyList<InflowDto> Inflows);

public sealed record AuditOverall(
    [property: JsonPropertyName("submitted")] long Submitted,
    [property: JsonPropertyName("rejected")] long Rejected,
    [property: JsonPropertyName("rejection_rate_pct")] double RejectionRatePct,
    [property: JsonPropertyName("total_applied_cents")] long TotalAppliedCents,
    [property: JsonPropertyName("total_approved_cents")] long TotalApprovedCents,
    [property: JsonPropertyName("reduction_cents")] long ReductionCents,
    [property: JsonPropertyName("avg_review_minutes")] double? AvgReviewMinutes,
    [property: JsonPropertyName("avg_settle_minutes")] double? AvgSettleMinutes);

public sealed record RejectionReason(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("count")] long Count);

public sealed record AuditMember(
    [property: JsonPropertyName("applicant_id")] string ApplicantId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("submitted")] long Submitted,
    [property: JsonPropertyName("rejected")] long Rejected,
    [property: JsonPropertyName("settled")] long Settled,
    [property: JsonPropertyName("applied_cents")] long AppliedCents,
    [property: JsonPropertyName("approved_cents")] long ApprovedCents,
    [property: JsonPropertyName("settled_cents")] long SettledCents,
    [property: JsonPropertyName("avg_review_minutes")] double? AvgReviewMinutes,
    [property: JsonPropertyName("avg_settle_minutes")] double? AvgSettleMinutes,
    [property: JsonPropertyName("channels")] Dictionary<string, long> Channels);

public sealed record AuditStatistics(
    [property: JsonPropertyName("overall")] AuditOverall Overall,
    [property: JsonPropertyName("rejection_reasons")] IReadOnlyList<RejectionReason> RejectionReasons,
    [property: JsonPropertyName("members")] IReadOnlyList<AuditMember> Members);

public sealed record BudgetMonth(
    [property: JsonPropertyName("month")] string Month,
    [property: JsonPropertyName("settled_by_channel")] Dictionary<string, long> SettledByChannel,
    [property: JsonPropertyName("inflow_cents")] long InflowCents);

public sealed record BudgetOverview(
    [property: JsonPropertyName("months")] IReadOnlyList<BudgetMonth> Months);
