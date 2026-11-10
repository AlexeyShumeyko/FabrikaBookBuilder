using System;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// The rule that keeps an order on the site the customer actually belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two installations share an API host and differ only by the site prefix, so a wrong
    /// prefix is not a broken address - it is a working order on the wrong site. The guard is
    /// therefore a check every write goes through, not a convention: the account's region and
    /// the client's region have to be the same object of comparison, and disagreement stops the
    /// operation before a request is made.
    /// </para>
    /// <para>
    /// There is deliberately no fallback. If the write fails, the failure is reported; trying
    /// the other site instead is exactly the mistake this guard exists to prevent.
    /// </para>
    /// </remarks>
    public static class SiteRegionGuard
    {
        public static Result EnsureSameRegion(SiteRegion accountRegion, SiteRegion targetRegion)
        {
            if (accountRegion is null)
                throw new ArgumentNullException(nameof(accountRegion));
            if (targetRegion is null)
                throw new ArgumentNullException(nameof(targetRegion));

            return string.Equals(accountRegion.Code, targetRegion.Code, StringComparison.OrdinalIgnoreCase)
                ? Result.Ok()
                : Result.Fail(
                    "Аккаунт относится к сайту " + accountRegion.DisplayName +
                    ", а действие адресовано сайту " + targetRegion.DisplayName +
                    ". Войдите в аккаунт того сайта, где нужно оформить заказ.");
        }
    }
}
