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

namespace TimezoneConvertorCmdPalExtension.Pages;

internal sealed partial class TimezoneConvertorCmdPalExtensionPage : DynamicListPage, IDisposable
{
    private bool _isError;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BufferBlock<string> _searchTextBuffer = new();
    private IReadOnlyList<ListItem> _results = GetAllTimeZonesWithLocalOnTop();

    public TimezoneConvertorCmdPalExtensionPage()
    {
        Icon = new IconInfo("\uEC92");
        Title = "Time zone Convertor for Command Palette";
        Name = "Open";

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

        var allTimeZones = GetAllTimeZonesWithLocalOnTop();

        // Filter time zones based on the search text
        var filteredTimeZones = string.IsNullOrWhiteSpace(searchText)
            ? allTimeZones
            : allTimeZones.Where(item => item.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                         item.Subtitle.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        cancellationToken.ThrowIfCancellationRequested();

        return filteredTimeZones.ToList();
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
            GetAllTimeZonesWithLocalOnTop();

            return new CommandItem
            {
                Title = "All time zones displayed",
                Icon = new IconInfo("\ue8af"),
                Subtitle = "Showing all time zones with the local time zone on top.",
                Command = new NoOpCommand()
            };
        }
    }

    private static List<ListItem> GetAllTimeZonesWithLocalOnTop()
    {
        var timeZones = TimeZoneNames.TZNames.GetDisplayNames(System.Globalization.CultureInfo.CurrentUICulture.Name);
        var localTimeZone = TimeZoneInfo.Local;

        var items = timeZones
            .Select(tz =>
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(tz.Key);
                var currentTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, timeZoneInfo);

                var timeAbbreviation = timeZoneInfo.IsDaylightSavingTime(currentTime)
                    ? TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(tz.Key,
                        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Daylight
                    : TimeZoneNames.TZNames.GetAbbreviationsForTimeZone(tz.Key,
                        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Standard;

                return new ListItem(new NoOpCommand())
                {
                    Title = $"{currentTime:hh:mm tt} {timeAbbreviation}",
                    Subtitle = tz.Value
                };
            })
            .ToList();

        // Move the local time zone to the top
        var localTimeZoneItem = items.FirstOrDefault(item => item.Subtitle == localTimeZone.DisplayName);
        if (localTimeZoneItem == null) return items;
        items.Remove(localTimeZoneItem);
        items.Insert(0, localTimeZoneItem);

        return items;
    }

}
