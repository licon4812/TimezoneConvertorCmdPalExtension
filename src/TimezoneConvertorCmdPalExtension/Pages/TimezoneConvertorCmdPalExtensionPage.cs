// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
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
                    _results = Array.Empty<ListItem>();
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

    private static async Task<IReadOnlyList<ListItem>> ProcessSearchAsync(string searchText, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Simulate asynchronous behavior

        // If the search text is empty, return all time zones
        var allTimeZones = GetAllTimeZonesWithLocalOnTop(DateTime.UtcNow);
        var localTimeZoneItem = allTimeZones.FirstOrDefault(item => item.Subtitle == TimeZoneInfo.Local.DisplayName);

        // Check if the search text is a valid date or time
        if (DateTime.TryParse(searchText, out var time))
        {
            allTimeZones = GetAllTimeZonesWithLocalOnTop(time);

            cancellationToken.ThrowIfCancellationRequested();

            return allTimeZones.ToList();
        }

        if (searchText.Contains("to"))
        {
            var parts = searchText.Split("to");
            if (DateTime.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate) ||
                DateTime.TryParse(parts[0], out parsedDate))
            {
                allTimeZones = GetAllTimeZonesWithLocalOnTop(parsedDate);
                localTimeZoneItem = allTimeZones.FirstOrDefault(item => item.Subtitle == TimeZoneInfo.Local.DisplayName);
            }

            // Additional filtering based on parts[1]
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                allTimeZones = allTimeZones.Where(item => item.Title.Contains(parts[1], StringComparison.OrdinalIgnoreCase) ||
                                                          item.Subtitle.Contains(parts[1], StringComparison.OrdinalIgnoreCase) ||
                                                          item.Subtitle.Contains(TimeZoneInfo.Local.DisplayName)) 
                    .ToList();
            }
        }
        else if (searchText.Contains(','))
        {
            var parts = searchText.Split(",");
            if (DateTime.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate) ||
                DateTime.TryParse(parts[0], out parsedDate))
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
                    $"UTC+{utcOffset.TotalHours:0}" : 
                    $"UTC{utcOffset.TotalHours:0}";

                return new ListItem(new NoOpCommand())
                {
                    Title = $"{currentTime:hh:mm tt} {timeAbbreviation}",
                    Subtitle = $"({offsetString}) {tz.Value} - {currentTime:D}",
                    Command = new CopyTextCommand($"{currentTime:hh:mm tt}"),
                };
            })
            .ToList();

        // Ensure the local timezone is always at the top
        var localTimeZoneItem = items.FirstOrDefault(item => item.Subtitle.Contains(localTimeZone.DisplayName));
        if (localTimeZoneItem == null) return items;
        items.Remove(localTimeZoneItem);
        items.Insert(0, localTimeZoneItem);
        return items;
    }

}
