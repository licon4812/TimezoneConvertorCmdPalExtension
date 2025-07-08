// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using NodaTime;
using NodaTime.TimeZones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Windows.ApplicationModel;

namespace TimezoneConvertorCmdPalExtension.Pages;

internal sealed partial class TimezoneConvertorCmdPalExtensionPage : DynamicListPage, IDisposable
{
    private bool _isError;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BufferBlock<string> _searchTextBuffer = new();
    private IReadOnlyList<ListItem> _results = GetAllTimeZonesWithLocalOnTop(DateTime.UtcNow);

    public TimezoneConvertorCmdPalExtensionPage()
    {

        // Retrieve the app version
        var version = Package.Current.Id.Version;
        var appVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        Icon = new IconInfo("\uE775");
        Title = $"Timezone Convertor - v{appVersion}";
        Name = "Convert";

        // Configure the search processing pipeline
        Task.Run(async () =>
        {
            while (await _searchTextBuffer.OutputAvailableAsync(_cancellationTokenSource.Token))
            {
                var searchText = await _searchTextBuffer.ReceiveAsync(_cancellationTokenSource.Token);
                IsLoading = true;
                try
                {
                    _results = await ProcessSearchAsync(searchText, _cancellationTokenSource.Token);
                    _isError = false;
                }
                catch
                {
                    _isError = true;
                    _results = [];
                }
                finally
                {
                    IsLoading = false;
                    RaiseItemsChanged(_results.Count);
                }
            }
        }, _cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (newSearch == oldSearch) return;

        _searchTextBuffer.Post(newSearch);
    }


    public override IListItem[] GetItems()
    {
        return _results.ToArray<IListItem>();
    }

    // Helper to parse date using local culture first, then fallback to Invariant (US)
    private static bool TryParseDateWithFallback(string input, out DateTime result)
    {
        // Try local culture first
        if (DateTime.TryParse(input, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out result))
            return true;
        // Fallback to US format (InvariantCulture)
        return DateTime.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out result);
    }

    private static async Task<IReadOnlyList<ListItem>> ProcessSearchAsync(string searchText, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Simulate asynchronous behavior

        // If the search text is empty, return all time zones
        var allTimeZones = GetAllTimeZonesWithLocalOnTop(DateTime.UtcNow);
        var localTimeZoneItem = allTimeZones.FirstOrDefault(item => item.Subtitle == TimeZoneInfo.Local.DisplayName);

        // Check if the search text is a valid date or time
        if (TryParseDateWithFallback(searchText, out var time))
        {
            allTimeZones = GetAllTimeZonesWithLocalOnTop(time);

            cancellationToken.ThrowIfCancellationRequested();

            return allTimeZones.ToList();
        }

        // Support for: <datetime>, <source timezone> to <target timezone>
        // and: <datetime> in<source timezone> to <target timezone>
        if (searchText.Contains("to", StringComparison.OrdinalIgnoreCase))
        {
            string beforeTo = searchText.Substring(0, searchText.IndexOf("to", StringComparison.OrdinalIgnoreCase)).Trim();
            string afterTo = searchText[(searchText.IndexOf("to", StringComparison.OrdinalIgnoreCase) + 2)..].Trim();

            string datePart = beforeTo;
            string sourceTzPart = null;

            // Handle ", <source timezone>" or "in<source timezone>"
            if (beforeTo.Contains(","))
            {
                var split = beforeTo.Split(',', 2);
                datePart = split[0].Trim();
                sourceTzPart = split[1].Trim();
            }
            else if (beforeTo.Contains(" in", StringComparison.OrdinalIgnoreCase))
            {
                var split = beforeTo.Split(new[] { " in" }, 2, StringSplitOptions.None);
                datePart = split[0].Trim();
                sourceTzPart = split[1].Trim();
            }

            if (TryParseDateWithFallback(datePart, out var parsedDate) && !string.IsNullOrWhiteSpace(sourceTzPart) && !string.IsNullOrWhiteSpace(afterTo))
            {
                var timeZoneNames = TimeZoneNames.TZNames.GetDisplayNames(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var sourceTzInfo = timeZoneNames.FirstOrDefault(tz => tz.Value.Contains(sourceTzPart, StringComparison.OrdinalIgnoreCase));
                var targetTzInfo = timeZoneNames.FirstOrDefault(tz => tz.Value.Contains(afterTo, StringComparison.OrdinalIgnoreCase));

                if (sourceTzInfo.Key != null && targetTzInfo.Key != null)
                {
                    var sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(sourceTzInfo.Key);
                    var targetTimeZone = TimeZoneInfo.FindSystemTimeZoneById(targetTzInfo.Key);

                    var dateTimeInSourceZone = DateTime.SpecifyKind(parsedDate, DateTimeKind.Unspecified);
                    var utcTime = TimeZoneInfo.ConvertTimeToUtc(dateTimeInSourceZone, sourceTimeZone);
                    var targetTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, targetTimeZone);
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);

                    // Build result items for target, source, and local
                    var targetAbbr = targetTimeZone.IsDaylightSavingTime(targetTime)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(targetTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(targetTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var targetOffset = targetTimeZone.GetUtcOffset(targetTime);
                    var targetOffsetString = targetOffset.TotalHours >= 0 ?
                        $"(UTC+{targetOffset.TotalHours:0}:00)" :
                        $"(UTC{targetOffset.TotalHours:0}:00)";
                    var targetItem = new ListItem(new NoOpCommand())
                    {
                        Title = $"{targetTime:hh:mm tt} {targetAbbr}",
                        Subtitle = $"{targetTzInfo.Value.Replace($"{targetOffsetString}", targetOffsetString)}, {GetCountriesFromTimeZoneAsAString(targetOffsetString)} - {targetTime:D}",
                        Command = new CopyTextCommand($"{targetTime:hh:mm tt}"),
                    };

                    var sourceAbbr = sourceTimeZone.IsDaylightSavingTime(dateTimeInSourceZone)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(sourceTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(sourceTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var sourceOffset = sourceTimeZone.GetUtcOffset(dateTimeInSourceZone);
                    var sourceOffsetString = sourceOffset.TotalHours >= 0 ?
                        $"(UTC+{sourceOffset.TotalHours:0}:00)" :
                        $"(UTC{sourceOffset.TotalHours:0}:00)";
                    var sourceItem = new ListItem(new NoOpCommand())
                    {
                        Title = $"{dateTimeInSourceZone:hh:mm tt} {sourceAbbr}",
                        Subtitle = $"{sourceTzInfo.Value.Replace($"{sourceOffsetString}", sourceOffsetString)}, {GetCountriesFromTimeZoneAsAString(sourceOffsetString)} - {dateTimeInSourceZone:D}",
                        Command = new CopyTextCommand($"{dateTimeInSourceZone:hh:mm tt}"),
                    };

                    var localAbbr = TimeZoneInfo.Local.IsDaylightSavingTime(localTime)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var localOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
                    var localOffsetString = localOffset.TotalHours >= 0 ?
                        $"(UTC+{localOffset.TotalHours:0}:00)" :
                        $"(UTC{localOffset.TotalHours:0}:00)";
                    var localTzName = timeZoneNames.FirstOrDefault(tz => tz.Key == TimeZoneInfo.Local.Id).Value ?? TimeZoneInfo.Local.DisplayName;
                    var localItem = new ListItem(new NoOpCommand())
                    {
                        Title = $"{localTime:hh:mm tt} {localAbbr}",
                        Subtitle = $"{localTzName.Replace($"{localOffsetString}", localOffsetString)}, {GetCountriesFromTimeZoneAsAString(localOffsetString)} - {localTime:D}",
                        Command = new CopyTextCommand($"{localTime:hh:mm tt}"),
                    };

                    // Only show target, source, and local
                    var result = new List<ListItem> { targetItem, sourceItem, localItem };
                    return result;
                }
            }
            // fallback to previous logic if not both timezones found
            if (TryParseDateWithFallback(datePart, out var fallbackDate))
            {
                allTimeZones = GetAllTimeZonesWithLocalOnTop(fallbackDate);
                localTimeZoneItem = allTimeZones.FirstOrDefault(item => item.Subtitle == TimeZoneInfo.Local.DisplayName);
            }
            if (!string.IsNullOrWhiteSpace(afterTo))
            {
                allTimeZones = allTimeZones.Where(item => item.Title.Contains(afterTo, StringComparison.OrdinalIgnoreCase) ||
                                                          item.Subtitle.Contains(afterTo, StringComparison.OrdinalIgnoreCase) ||
                                                          item.Subtitle.Contains(TimeZoneInfo.Local.DisplayName))
                    .ToList();
            }
        }
        // Support for: <datetime> in <source timezone>
        else if (searchText.Contains(" in ", StringComparison.OrdinalIgnoreCase))
        {
            var split = searchText.Split(new[] { " in " }, 2, StringSplitOptions.None);
            var datePart = split[0].Trim();
            var sourceTzPart = split[1].Trim();
            if (TryParseDateWithFallback(datePart, out var parsedDate) && !string.IsNullOrWhiteSpace(sourceTzPart))
            {
                var timeZoneNames = TimeZoneNames.TZNames.GetDisplayNames(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var sourceTzInfo = timeZoneNames.FirstOrDefault(tz => tz.Value.Contains(sourceTzPart, StringComparison.OrdinalIgnoreCase));
                if (sourceTzInfo.Key != null)
                {
                    var sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(sourceTzInfo.Key);
                    var dateTimeInSourceZone = DateTime.SpecifyKind(parsedDate, DateTimeKind.Unspecified);
                    var utcTime = TimeZoneInfo.ConvertTimeToUtc(dateTimeInSourceZone, sourceTimeZone);
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);

                    // Build result items for source and local
                    var sourceAbbr = sourceTimeZone.IsDaylightSavingTime(dateTimeInSourceZone)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(sourceTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(sourceTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var sourceOffset = sourceTimeZone.GetUtcOffset(dateTimeInSourceZone);
                    var sourceOffsetString = sourceOffset.TotalHours >= 0 ?
                        $"(UTC+{sourceOffset.TotalHours:0}:00)" :
                        $"(UTC{sourceOffset.TotalHours:0}:00)";
                    var sourceItem = new ListItem(new NoOpCommand())
                    {
                        Title = $"{dateTimeInSourceZone:hh:mm tt} {sourceAbbr}",
                        Subtitle = $"{sourceTzInfo.Value.Replace($"{sourceOffsetString}", sourceOffsetString)}, {GetCountriesFromTimeZoneAsAString(sourceOffsetString)} - {dateTimeInSourceZone:D}",
                        Command = new CopyTextCommand($"{dateTimeInSourceZone:hh:mm tt}"),
                    };

                    var localAbbr = TimeZoneInfo.Local.IsDaylightSavingTime(localTime)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var localOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
                    var localOffsetString = localOffset.TotalHours >= 0 ?
                        $"(UTC+{localOffset.TotalHours:0}:00)" :
                        $"(UTC{localOffset.TotalHours:0}:00)";
                    var localTzName = timeZoneNames.FirstOrDefault(tz => tz.Key == TimeZoneInfo.Local.Id).Value ?? TimeZoneInfo.Local.DisplayName;
                    var localItem = new ListItem(new NoOpCommand())
                    {
                        Title = $"{localTime:hh:mm tt} {localAbbr}",
                        Subtitle = $"{localTzName.Replace($"{localOffsetString}", localOffsetString)}, {GetCountriesFromTimeZoneAsAString(localOffsetString)} - {localTime:D}",
                        Command = new CopyTextCommand($"{localTime:hh:mm tt}"),
                    };

                    // Only show source and local
                    var result = new List<ListItem> { sourceItem, localItem };
                    return result;
                }
            }
        }
        else if (searchText.Contains(','))
        {
            var parts = searchText.Split(",");
            if (TryParseDateWithFallback(parts[0], out var parsedDate))
            {
                var timeZoneNames = TimeZoneNames.TZNames.GetDisplayNames(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var timeZoneInfo = timeZoneNames.FirstOrDefault(tz => tz.Value.Contains(parts[1].Trim(), StringComparison.OrdinalIgnoreCase));

                if (timeZoneInfo.Key != null)
                {
                    // Parse the date/time as unspecified (not assuming local timezone)
                    var sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneInfo.Key);
                    
                    // Treat the parsed date as if it's in the source timezone
                    var dateTimeInSourceZone = DateTime.SpecifyKind(parsedDate, DateTimeKind.Unspecified);
                    var sourceTime = TimeZoneInfo.ConvertTimeToUtc(dateTimeInSourceZone, sourceTimeZone);
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(sourceTime, TimeZoneInfo.Local);

                    // Update the list of time zones with the converted local time
                    allTimeZones = GetAllTimeZonesWithLocalOnTop(localTime);

                    // Find the item for the specified time zone
                    var specifiedTimeZoneItem = allTimeZones.FirstOrDefault(item => item.Subtitle.Contains(parts[1].Trim(), StringComparison.OrdinalIgnoreCase));
                    localTimeZoneItem = allTimeZones.FirstOrDefault(item => item.Subtitle == TimeZoneInfo.Local.DisplayName);

                    // Reorder the list: specified time zone -> local time zone -> others
                    if (specifiedTimeZoneItem != null)
                    {
                        allTimeZones.Remove(specifiedTimeZoneItem);
                        allTimeZones.Insert(0, specifiedTimeZoneItem);
                    }

                    if (localTimeZoneItem != null && !allTimeZones.Contains(localTimeZoneItem))
                    {
                        allTimeZones.Remove(localTimeZoneItem);
                        allTimeZones.Insert(1, localTimeZoneItem); // Place local time zone after the specified time zone
                    }
                }
            }
        }
        else
        {
            // Final else block for general filtering
            allTimeZones = allTimeZones.Where(item => item.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                                      item.Subtitle.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                                      item.Subtitle.Contains(TimeZoneInfo.Local.DisplayName))
                .ToList();
        }

        // Ensure the local timezone is not removed
        if (localTimeZoneItem != null && !allTimeZones.Contains(localTimeZoneItem))
        {
            allTimeZones.Insert(0, localTimeZoneItem);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return allTimeZones.ToList();
    }

    public override ICommandItem? EmptyContent
    {
        get
        {
            if (_isError)
            {
                return new CommandItem
                {
                    Title = "Error loading time zones",
                    Icon = new IconInfo("\uea39"),
                    Subtitle = "An error occurred while fetching time zones.",
                    Command = new NoOpCommand()
                };
            }

            // Use the refactored method to fetch all time zones
            GetAllTimeZonesWithLocalOnTop(DateTime.UtcNow);

            return new CommandItem
            {
                Title = "All time zones displayed",
                Icon = new IconInfo("\ue8af"),
                Subtitle = "Showing all time zones with the local time zone on top.",
                Command = new NoOpCommand()
            };
        }
    }

    private static List<ListItem> GetAllTimeZonesWithLocalOnTop(DateTime dateTime)
    {
        var timeZones = TimeZoneNames.TZNames.GetDisplayNames(System.Globalization.CultureInfo.CurrentUICulture.Name);
        var localTimeZone = TimeZoneInfo.Local;
        int localIndex = -1;
        int i = 0;

        var items = timeZones
            .Select(tz =>
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(tz.Key);
                var currentTime = TimeZoneInfo.ConvertTime(dateTime, timeZoneInfo);

                var timeAbbreviation = timeZoneInfo.IsDaylightSavingTime(currentTime)
                    ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(tz.Key,
                        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                    : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(tz.Key,
                        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;

                // Get the UTC offset for the current time, accounting for DST
                var utcOffset = timeZoneInfo.GetUtcOffset(currentTime);
                var offsetString = utcOffset.TotalHours >= 0 ?
                    $"(UTC+{utcOffset.TotalHours:0}:00)" :
                    $"(UTC{utcOffset.TotalHours:0}:00)";


                // Fix CA1310 and CA1866 by using IndexOf(char, StringComparison)
                var startIndex = tz.Value.IndexOf('(', StringComparison.Ordinal);
                var endIndex = tz.Value.IndexOf(')', StringComparison.Ordinal);
                var subString = startIndex >= 0 && endIndex > startIndex
                    ? tz.Value.Substring(startIndex, endIndex - startIndex + 1)
                    : string.Empty;

                if (tz.Key == localTimeZone.Id)
                {
                    localIndex = i;
                }
                i++;

                return new ListItem(new NoOpCommand())
                {
                    Title = $"{currentTime:hh:mm tt} {timeAbbreviation}",
                    Subtitle = $"{tz.Value.Replace($"{subString}", offsetString)}, {GetCountriesFromTimeZoneAsAString(offsetString)} - {currentTime:D}",
                    Command = new CopyTextCommand($"{currentTime:hh:mm tt}"),
                };
            })
            .ToList();

        // Ensure the local timezone is always at the top by timeZoneId
        if (localIndex > 0 && localIndex < items.Count)
        {
            var localTimeZoneItem = items[localIndex];
            items.RemoveAt(localIndex);
            items.Insert(0, localTimeZoneItem);
        }
        return items;
    }

    private static string GetCountriesFromTimeZoneAsAString(string timezoneOffset)
    {
        // Define the UTC offset you're interested in
        var targetOffset = Offset.FromHours(10); // e.g., UTC+10

        // Get the TZDB source and all zone locations
        var tzdb = TzdbDateTimeZoneSource.Default;
        var now = SystemClock.Instance.GetCurrentInstant();

        // Check for null before using ZoneLocations
        if (tzdb.ZoneLocations == null)
        {
            return string.Empty;
        }

        var countries = tzdb.ZoneLocations
            .Where(loc =>
            {
                var zone = DateTimeZoneProviders.Tzdb[loc.ZoneId];
                var interval = zone.GetZoneInterval(now);
                return interval.StandardOffset == targetOffset || interval.Savings == targetOffset;
            }).Select(loc => loc.CountryName).Distinct().ToList();
        return countries.Count > 0 ? $"{string.Join(", ", countries)}" : string.Empty;
    }
}
