using System;
using Models.Gameplay.Campaign;

namespace Engine.Service
{
    /// <summary>
    /// Canonical short identifiers shared by the simulation log and the play
    /// scene inspector. One spelling per entity keeps a single grep sufficient
    /// when reading logs back.
    /// </summary>
    public static class SimLogNames
    {
        public const string UnknownId = "------";
        public const string UnknownFlight = "----------";

        public static string ShortId(Guid id)
        {
            return id == Guid.Empty
                ? UnknownId
                : id.ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        public static string AllianceCode(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => "BLU",
                Alliance.Redfor => "RED",
                _ => "NEU"
            };
        }

        public static string FlightLabel(Alliance alliance, Guid flightId)
        {
            return flightId == Guid.Empty
                ? UnknownFlight
                : $"{AllianceCode(alliance)}-{ShortId(flightId)}";
        }

        public static string PackageLabel(Guid packageId)
        {
            return $"PKG-{ShortId(packageId)}";
        }

        public static string PlanLabel(Guid planId)
        {
            return $"PLAN-{ShortId(planId)}";
        }

        public static string SiteLabel(Guid siteId)
        {
            return $"SAM-{ShortId(siteId)}";
        }

        /// <summary>
        /// Collapses whitespace so a detail string can never break the
        /// one-event-per-line contract the log format depends on.
        /// </summary>
        public static string SingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new System.Text.StringBuilder(text.Length);
            var lastWasSpace = false;
            foreach (var character in text)
            {
                var isSpace = char.IsWhiteSpace(character);
                if (isSpace)
                {
                    if (!lastWasSpace && builder.Length > 0)
                        builder.Append(' ');
                }
                else
                {
                    builder.Append(character);
                }

                lastWasSpace = isSpace;
            }

            return builder.ToString().TrimEnd();
        }
    }
}
