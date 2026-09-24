using Avalonia.Controls;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record AuditRowVm(
    string Name, string Submitted, string Rejected, string Settled, string AppliedText,
    string ApprovedText, string SettledText, string Channels, string AvgReview, string AvgSettle);

public partial class AuditPage : UserControl
{
    public AuditPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var isSecretary = AppServices.Session?.Role == "BRANCH_SECRETARY";
        DeniedCard.IsVisible = !isSecretary;
        OverallCards.IsVisible = isSecretary;
        ReasonsCard.IsVisible = isSecretary;
        FooterText.Text = "";

        if (!isSecretary || !AppServices.NativeReady || AppServices.Session is null)
        {
            return;
        }

        try
        {
            var stats = await Task.Run(() =>
                AppServices.Client.GetAuditStatistics(AppServices.Session!.UserId));
            var overall = stats.Overall;

            SubmittedValue.Text = overall.Submitted.ToString();
            RejectionRateValue.Text = $"{overall.RejectionRatePct:0.0}%";
            ReductionValue.Text = RectaClient.FormatMoney(overall.ReductionCents);
            AvgReviewValue.Text = FormatMinutes(overall.AvgReviewMinutes);
            AvgSettleValue.Text = FormatMinutes(overall.AvgSettleMinutes);

            ReasonsText.Text = stats.RejectionReasons.Count == 0
                ? "暂无驳回记录。"
                : string.Join(";  ", stats.RejectionReasons.Select(r => $"{r.Reason} ×{r.Count}"));

            Rows.ItemsSource = stats.Members.Select(m => new AuditRowVm(
                m.DisplayName,
                m.Submitted.ToString(),
                m.Rejected.ToString(),
                m.Settled.ToString(),
                RectaClient.FormatMoney(m.AppliedCents),
                RectaClient.FormatMoney(m.ApprovedCents),
                RectaClient.FormatMoney(m.SettledCents),
                string.Join(" ", m.Channels.Select(kv =>
                    (kv.Key switch
                    {
                        "FLEXIBLE" => "灵活",
                        "FACULTY" => "系报",
                        "CLASS_FUND" => "班费",
                        _ => kv.Key,
                    }) + kv.Value)),
                FormatMinutes(m.AvgReviewMinutes),
                FormatMinutes(m.AvgSettleMinutes))).ToList();

            FooterText.Text =
                $"全员提单 {overall.Submitted} 项 | 驳回 {overall.Rejected} 项 | " +
                $"申报合计 {RectaClient.FormatMoney(overall.TotalAppliedCents)} 元 | " +
                $"核准合计 {RectaClient.FormatMoney(overall.TotalApprovedCents)} 元";
        }
        catch (RectaException ex)
        {
            FooterText.Text = $"加载失败:{ex.Message}";
        }
    }

    private static string FormatMinutes(double? minutes) => minutes switch
    {
        null => "—",
        < 1 => $"{minutes.Value:0.0} 分",
        < 60 => $"{minutes.Value:0.#} 分",
        < 1440 => $"{minutes.Value / 60:0.#} 时",
        _ => $"{minutes.Value / 1440:0.#} 天",
    };
}
