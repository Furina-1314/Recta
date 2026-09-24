#include "recta/Conservation.hpp"

#include <format>

namespace recta {

CustodyTotals ComputeCustodyTotals(const std::vector<Money>& balances) {
    CustodyTotals totals;
    for (const Money balance : balances) {
        totals.balances_sum += balance;
        if (balance.to_cents() > 0) {
            totals.custodian_cash += balance;
        } else if (balance.to_cents() < 0) {
            totals.advance_total += balance.abs();
        }
    }
    return totals;
}

bool IsConserved(Money balances_sum, Money custodian_cash, Money advance_total) {
    return balances_sum == (custodian_cash - advance_total);
}

void VerifyConservation(const std::vector<Money>& balances) {
    const CustodyTotals totals = ComputeCustodyTotals(balances);
    if (!IsConserved(totals.balances_sum, totals.custodian_cash, totals.advance_total)) {
        throw std::logic_error(std::format(
            "对账守恒被破坏：Σb={} 而 C_cash−A_advance={}",
            totals.balances_sum.to_plain_string(),
            (totals.custodian_cash - totals.advance_total).to_plain_string()));
    }
}

} // namespace recta
