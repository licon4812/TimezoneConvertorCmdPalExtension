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
    private bool _isError = false;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BufferBlock<string> _searchTextBuffer = new();
    private IReadOnlyList<ListItem> _results = Array.Empty<ListItem>();

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

    private async Task<IReadOnlyList<ListItem>> ProcessSearchAsync(string searchText, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Simulate asynchronous behavior
        var timeZones = TimeZoneInfo.GetSystemTimeZones();

        // Filter time zones based on the search text
        var filteredTimeZones = string.IsNullOrWhiteSpace(searchText)
            ? timeZones
            : timeZones.Where(tz => tz.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        cancellationToken.ThrowIfCancellationRequested();

        var localTimeZone = TimeZoneInfo.Local;

        // Ensure the local time zone is always included
        if (!filteredTimeZones.Contains(localTimeZone))
        {
            filteredTimeZones = filteredTimeZones.Append(localTimeZone);
        }

        var items = filteredTimeZones
            .Select(tz => new ListItem(new NoOpCommand())
            {
                Title = $"{tz.DisplayName} - {TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz):hh:mm tt}"
            })
            .ToList();

        // Move the local time zone to the top
        var localTimeZoneItem = items.FirstOrDefault(item => item.Title.StartsWith(localTimeZone.DisplayName, StringComparison.Ordinal));
        if (localTimeZoneItem != null)
        {
            items.Remove(localTimeZoneItem);
            items.Insert(0, localTimeZoneItem);
        }

        return items;
    }

    public override IListItem[] GetItems()
    {
        return _results.ToArray<IListItem>();
    }

    public override ICommandItem? EmptyContent => new CommandItem
    {
        Title = _isError ? "Error loading time zones" : "No time zones found",
        Icon = _isError ? new IconInfo("\uea39") : new IconInfo("\ue8af"),
        Subtitle = _isError ? "An error occurred while fetching time zones." : "Try searching for a different time zone.",
        Command = new NoOpCommand()
    };
}
