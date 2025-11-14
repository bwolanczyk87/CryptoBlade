using System;
using System.Threading.Tasks;
using CryptoBlade.Models;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Helpery sesyjne i narzędziowe dla Sigmy:
    /// - sortowanie świec po czasie,
    /// - wyznaczanie kotwicy sesji (anchor),
    /// - wybór ceny referencyjnej do ATR%,
    /// - bezpieczne pobieranie wartości z providera z zamianą błędów na NaN.
    ///
    /// Wszystkie funkcje są czyste poza TryGetOrNaNAsync, która opakowuje I/O.
    /// </summary>
    public static class SessionHelpers
    {
        /// <summary>
        /// Upewnia się, że tablica świec jest posortowana rosnąco po Date.
        /// Wykrywa tylko prostą niespójność (gdy którykolwiek element jest "wstecz")
        /// i wtedy wykonuje pełne sortowanie.
        /// 
        /// Dla null lub długości &lt; 2 nie robi nic.
        /// </summary>
        public static void EnsureSortedByDate(Quote[]? quotes)
        {
            if (quotes == null || quotes.Length < 2)
                return;

            for (int i = 1; i < quotes.Length; i++)
            {
                if (quotes[i].Date < quotes[i - 1].Date)
                {
                    Array.Sort(quotes, (a, b) => a.Date.CompareTo(b.Date));
                    break;
                }
            }
        }

        /// <summary>
        /// Wyznacza kotwicę sesji (anchor) w czasie UTC na podstawie:
        /// - ostatniego dostępnego bara 1m (jeśli dostępny),
        /// - lub DateTime.UtcNow, gdy brak świec.
        ///
        /// anchor = dzień (UTC) z godziną sessionStartHourUtc. 
        /// Jeśli ostatni bar jest wcześniejszy niż anchor tego dnia, 
        /// cofamy się do anchor dnia poprzedniego.
        ///
        /// Przykład:
        /// - sessionStartHourUtc = 0 => anchor to północ UTC,
        /// - sessionStartHourUtc = 8 => "sesja europejska" od 08:00 UTC.
        /// </summary>
        public static DateTime GetCurrentSessionAnchorUtc(
            Quote[]? oneMinuteQuotes,
            int sessionStartHourUtc)
        {
            DateTime last =
                (oneMinuteQuotes != null && oneMinuteQuotes.Length > 0)
                    ? oneMinuteQuotes[^1].Date
                    : DateTime.UtcNow;

            var dayAnchor = new DateTime(
                    last.Year,
                    last.Month,
                    last.Day,
                    0, 0, 0,
                    DateTimeKind.Utc)
                .AddHours(sessionStartHourUtc);

            // Jeśli ostatni bar jest "przed" kotwicą tego dnia,
            // to tak naprawdę jesteśmy jeszcze w sesji z poprzedniego dnia.
            if (last < dayAnchor)
                dayAnchor = dayAnchor.AddDays(-1);

            return dayAnchor;
        }

        /// <summary>
        /// Wybiera cenę referencyjną do obliczeń ATR%:
        /// - preferuje ticker.LastPrice, jeżeli &gt; 0,
        /// - w przeciwnym razie bierze Close z ostatniej świecy z referencyjnego interwału,
        /// - jeżeli brak danych, zwraca NaN.
        /// </summary>
        public static double SelectReferencePrice(
            Ticker? ticker,
            Quote[]? referenceQuotes)
        {
            if (ticker != null && ticker.LastPrice > 0m)
                return (double)ticker.LastPrice;

            if (referenceQuotes != null && referenceQuotes.Length > 0)
                return (double)referenceQuotes[^1].Close;

            return double.NaN;
        }

        /// <summary>
        /// Bezpiecznie pobiera wartość double z asynchronicznego providera.
        ///
        /// Zasady:
        /// - Jeśli wywołanie f() rzuci wyjątek => zwraca double.NaN.
        /// - Jeśli wynik jest NaN/Infinity => zwraca double.NaN.
        /// - W przeciwnym razie zwraca wynik bez zmian.
        ///
        /// Przydatne jako cienka warstwa do "brudnego" I/O z giełdy, żeby na wejściu
        /// do modelu cech zawsze mieć spójną semantykę NaN="brak".
        /// </summary>
        public static async Task<double> TryGetOrNaNAsync(
            Func<Task<double>> providerCall)
        {
            if (providerCall == null)
                throw new ArgumentNullException(nameof(providerCall));

            try
            {
                var v = await providerCall().ConfigureAwait(false);
                return (double.IsNaN(v) || double.IsInfinity(v)) ? double.NaN : v;
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
