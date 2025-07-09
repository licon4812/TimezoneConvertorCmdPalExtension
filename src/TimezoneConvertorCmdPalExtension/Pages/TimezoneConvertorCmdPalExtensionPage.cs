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
using NodaTime.Text;
using System.Text.RegularExpressions;

namespace TimezoneConvertorCmdPalExtension.Pages;

internal sealed partial class TimezoneConvertorCmdPalExtensionPage : DynamicListPage, IDisposable
{
    private bool _isError;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BufferBlock<string> _searchTextBuffer = new();
    private IReadOnlyList<ListItem> _results;

    [GeneratedRegex(@"UTC([+-]\d{1,2})(?::(\d{2}))?")]
    private static partial Regex TimezoneOffsetRegex();

    public TimezoneConvertorCmdPalExtensionPage()
    {
        // Retrieve the app version
        var version = Package.Current.Id.Version;
        var appVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        Icon = new IconInfo("\uE775");
        Title = $"Timezone Convertor - v{appVersion}";
        Name = "Convert";

        // Set initial results with local timezone on top
        _results = GetAllTimeZonesWithLocalOnTop(DateTime.Now);

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
            string? sourceTzPart = null;

            // Handle ", <source timezone>" or "in<source timezone>"
            if (beforeTo.Contains(','))
            {
                var split = beforeTo.Split(',', 2);
                datePart = split[0].Trim();
                sourceTzPart = split[1].Trim();
            }
            else if (beforeTo.Contains(" in", StringComparison.OrdinalIgnoreCase))
            {
                var split = beforeTo.Split([" in"], 2, StringSplitOptions.None);
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
                    var targetOffsetString = targetOffset.TotalMinutes >= 0 ?
                        $"(UTC+{Math.Abs(targetOffset.Hours)}:{Math.Abs(targetOffset.Minutes):00})" :
                        $"(UTC-{Math.Abs(targetOffset.Hours)}:{Math.Abs(targetOffset.Minutes):00})";
                    var targetItem = new ListItem(new NoOpCommand())
                    {
                        Title = string.IsNullOrEmpty(targetAbbr)
                            ? $"{targetTime:hh:mm tt}"
                            : $"{targetTime:hh:mm tt} {targetAbbr}",
                        Subtitle = $"{targetTzInfo.Value.Replace($"{targetOffsetString}", targetOffsetString)}, {GetCountriesFromTimeZoneAsAString(targetTzInfo, targetTime)} - {targetTime:D}",
                        Command = new CopyTextCommand($"{targetTime:hh:mm tt}"),
                    };

                    var sourceAbbr = sourceTimeZone.IsDaylightSavingTime(dateTimeInSourceZone)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(sourceTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(sourceTzInfo.Key, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var sourceOffset = sourceTimeZone.GetUtcOffset(dateTimeInSourceZone);
                    var sourceOffsetString = sourceOffset.TotalMinutes >= 0 ?
                        $"(UTC+{Math.Abs(sourceOffset.Hours)}:{Math.Abs(sourceOffset.Minutes):00})" :
                        $"(UTC-{Math.Abs(sourceOffset.Hours)}:{Math.Abs(sourceOffset.Minutes):00})";
                    var sourceItem = new ListItem(new NoOpCommand())
                    {
                        Title = string.IsNullOrEmpty(sourceAbbr)
                            ? $"{dateTimeInSourceZone:hh:mm tt}"
                            : $"{dateTimeInSourceZone:hh:mm tt} {sourceAbbr}",
                        Subtitle = $"{sourceTzInfo.Value.Replace($"{sourceOffsetString}", sourceOffsetString)}, {GetCountriesFromTimeZoneAsAString(sourceTzInfo, dateTimeInSourceZone)} - {dateTimeInSourceZone:D}",
                        Command = new CopyTextCommand($"{dateTimeInSourceZone:hh:mm tt}"),
                    };

                    var localAbbr = TimeZoneInfo.Local.IsDaylightSavingTime(localTime)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var localOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
                    var localOffsetString = localOffset.TotalMinutes >= 0 ?
                        $"(UTC+{Math.Abs(localOffset.Hours)}:{Math.Abs(localOffset.Minutes):00})" :
                        $"(UTC-{Math.Abs(localOffset.Hours)}:{Math.Abs(localOffset.Minutes):00})";
                    var localTzName = timeZoneNames.FirstOrDefault(tz => tz.Key == TimeZoneInfo.Local.Id).Value ?? TimeZoneInfo.Local.DisplayName;
                    var localItem = new ListItem(new NoOpCommand())
                    {
                        Title = string.IsNullOrEmpty(localAbbr)
                            ? $"{localTime:hh:mm tt}"
                            : $"{localTime:hh:mm tt} {localAbbr}",
                        Subtitle = $"{localTzName.Replace($"{localOffsetString}", localOffsetString)}, {GetCountriesFromTimeZoneAsAString(new KeyValuePair<string, string>(TimeZoneInfo.Local.Id, localTzName), localTime)} - {localTime:D}",
                        Command = new CopyTextCommand($"{localTime:hh:mm tt}"),
                    };

                    // Only show target, source, and local if their abbreviations are not null or empty
                    var result = new List<ListItem>();
                    if (!string.IsNullOrEmpty(targetAbbr))
                    {
                        result.Add(targetItem);
                    }
                    if (!string.IsNullOrEmpty(sourceAbbr))
                    {
                        result.Add(sourceItem);
                    }
                    if (!string.IsNullOrEmpty(localAbbr))
                    {
                        result.Add(localItem);
                    }
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
            var split = searchText.Split([" in "], 2, StringSplitOptions.None);
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
                    var sourceOffsetString = sourceOffset.TotalMinutes >= 0 ?
                        $"(UTC+{Math.Abs(sourceOffset.Hours)}:{Math.Abs(sourceOffset.Minutes):00})" :
                        $"(UTC-{Math.Abs(sourceOffset.Hours)}:{Math.Abs(sourceOffset.Minutes):00})";
                    var sourceItem = new ListItem(new NoOpCommand())
                    {
                        Title = string.IsNullOrEmpty(sourceAbbr)
                            ? $"{dateTimeInSourceZone:hh:mm tt}"
                            : $"{dateTimeInSourceZone:hh:mm tt} {sourceAbbr}",
                        Subtitle = $"{sourceTzInfo.Value.Replace($"{sourceOffsetString}", sourceOffsetString)} {GetCountriesFromTimeZoneAsAString(sourceTzInfo, dateTimeInSourceZone)} - {dateTimeInSourceZone:D}",
                        Command = new CopyTextCommand($"{dateTimeInSourceZone:hh:mm tt}"),
                    };

                    var localAbbr = TimeZoneInfo.Local.IsDaylightSavingTime(localTime)
                        ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                        : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;
                    var localOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
                    var localOffsetString = localOffset.TotalMinutes >= 0 ?
                        $"(UTC+{Math.Abs(localOffset.Hours)}:{Math.Abs(localOffset.Minutes):00})" :
                        $"(UTC-{Math.Abs(localOffset.Hours)}:{Math.Abs(localOffset.Minutes):00})";
                    var localTzName = timeZoneNames.FirstOrDefault(tz => tz.Key == TimeZoneInfo.Local.Id).Value ?? TimeZoneInfo.Local.DisplayName;
                    var localItem = new ListItem(new NoOpCommand())
                    {
                        Title = string.IsNullOrEmpty(localAbbr)
                            ? $"{localTime:hh:mm tt}"
                            : $"{localTime:hh:mm tt} {localAbbr}",
                        Subtitle = $"{localTzName.Replace($"{localOffsetString}", localOffsetString)} {GetCountriesFromTimeZoneAsAString(new KeyValuePair<string, string>(TimeZoneInfo.Local.Id, localTzName), localTime)} - {localTime:D}",
                        Command = new CopyTextCommand($"{localTime:hh:mm tt}"),
                    };

                    // Only show source and local if their abbreviations are not null or empty
                    var result = new List<ListItem>();
                    if (!string.IsNullOrEmpty(sourceAbbr))
                    {
                        result.Add(sourceItem);
                    }
                    if (!string.IsNullOrEmpty(localAbbr))
                    {
                        result.Add(localItem);
                    }
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
            allTimeZones = allTimeZones.Where(item =>
                item.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(TimeZoneInfo.Local.DisplayName) ||
                // Fallback: match abbreviation, but only if it's a full word match
                item.Title.Split(' ').Any(word => word.Equals(searchText, StringComparison.OrdinalIgnoreCase))
            ).ToList();
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
        var localTimeZoneId = TimeZoneInfo.Local.Id;

        // Build a list of (ListItem, timeZoneId) only for valid time zones
        var itemsWithId = new List<(ListItem, string)>();
        foreach (var tz in timeZones)
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
            var offsetSign = utcOffset.TotalMinutes >= 0 ? "+" : "-";
            var absHours = Math.Abs(utcOffset.Hours);
            var absMinutes = Math.Abs(utcOffset.Minutes);
            var offsetString = $"(UTC{offsetSign}{absHours}:{absMinutes:00})";
            var startIndex = tz.Value.IndexOf('(', StringComparison.Ordinal);
            var endIndex = tz.Value.IndexOf(')', StringComparison.Ordinal);
            var subString = startIndex >= 0 && endIndex > startIndex
                ? tz.Value.Substring(startIndex, endIndex - startIndex + 1)
                : string.Empty;
            // Always include the local time zone, even if abbreviation is empty
            if (tz.Key != localTimeZoneId && string.IsNullOrEmpty(timeAbbreviation))
                continue;

            // Convert Windows ID to IANA for NodaTime usage
            string ianaZoneId;
            try
            {
                ianaZoneId = TimeZoneConverter.TZConvert.WindowsToIana(tz.Key);
            }
            catch
            {
                // Skip if conversion fails
                continue;
            }

            var item = new ListItem(new NoOpCommand())
            {
                Title = $"{currentTime:hh:mm tt} {timeAbbreviation}",
                Subtitle = $"{tz.Value.Replace($"{subString}", offsetString)} {GetCountriesFromTimeZoneAsAString(new KeyValuePair<string, string>(tz.Key, tz.Value), currentTime)} - {currentTime:D}",
                Command = new CopyTextCommand($"{currentTime:hh:mm tt}"),
            };
            itemsWithId.Add((item, tz.Key));
        }
        // Find the local time zone item by ID
        int localIdx = itemsWithId.FindIndex(x => x.Item2 == localTimeZoneId);
        if (localIdx > 0)
        {
            var localItem = itemsWithId[localIdx];
            itemsWithId.RemoveAt(localIdx);
            itemsWithId.Insert(0, localItem);
        }
        return itemsWithId.Select(x => x.Item1).ToList();
    }

    private static string GetCountriesFromTimeZoneAsAString(KeyValuePair<string, string> tz, DateTime currentTime)
    {
        var tzdb = TzdbDateTimeZoneSource.Default;
        string zoneId;
        try
        {
            zoneId = TimeZoneConverter.TZConvert.WindowsToIana(tz.Key);
        }
        catch
        {
            // If conversion fails, return empty
            return string.Empty;
        }
        var displayName = tz.Value;
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var interval = zone.GetZoneInterval(Instant.FromDateTimeUtc(currentTime.ToUniversalTime()));
        var abbrs = TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(zoneId, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        // Match both standard and daylight abbreviations
        var countries = tzdb.ZoneLocations
            .Where(loc =>
            {
                var z = DateTimeZoneProviders.Tzdb[loc.ZoneId];
                var i = z.GetZoneInterval(Instant.FromDateTimeUtc(currentTime.ToUniversalTime()));
                return
                    (!string.IsNullOrEmpty(abbrs.Standard) && i.Name.Equals(abbrs.Standard, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(abbrs.Daylight) && i.Name.Equals(abbrs.Daylight, StringComparison.OrdinalIgnoreCase));
            })
            .Select(loc => loc.CountryName).Distinct().ToList();

        var countriesString = countries.Count == 0 ? string.Empty : string.Join(", ", countries);
        return string.IsNullOrEmpty(countriesString) ? string.Empty : $", {countriesString}";
    }
}
