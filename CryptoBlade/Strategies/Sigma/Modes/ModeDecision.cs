namespace CryptoBlade.Strategies.Sigma.Modes
{
    public readonly struct ModeDecision(bool buy, bool sell, bool buyExtra, bool sellExtra)
    {
        public readonly bool HasBuy = buy, HasSell = sell, HasBuyExtra = buyExtra, HasSellExtra = sellExtra;
        public static ModeDecision None => new(false, false, false, false);
    }
}
