using System.Text;

namespace Powersuit.DeveloperConsole.UnityAdapters
{
    /// <summary>
    /// Extension point for gameplay-specific statistics. Providers append to a
    /// reused builder only at the overlay's low-frequency refresh interval.
    /// </summary>
    public interface IDeveloperStatisticsProvider
    {
        void AppendDeveloperStatistics(StringBuilder builder);
    }
}
