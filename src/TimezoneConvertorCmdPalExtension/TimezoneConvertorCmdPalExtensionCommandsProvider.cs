// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TimezoneConvertorCmdPalExtension;

public partial class TimezoneConvertorCmdPalExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public TimezoneConvertorCmdPalExtensionCommandsProvider()
    {
        DisplayName = "Time zone Convertor";
        Icon = new IconInfo("\uEC92");
        _commands = [
            new CommandItem(new Pages.TimezoneConvertorCmdPalExtensionPage()) { Title = DisplayName },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
