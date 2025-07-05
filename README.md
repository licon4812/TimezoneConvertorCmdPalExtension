# Time Zone ConvertorCmdPalExtension

Time Zone Convertor CmdPalExtension is an extension for the PowerToys Command Palette that allows users to quickly convert time between different time zones.

## Features

- Effortlessly convert time between various time zones using the Command Palette.
- Simplify scheduling across multiple time zones.
- Support for a wide range of global time zones.
- Automatic Daylight Saving Time (DST) handling - displays correct UTC offsets based on the date.
- Shows timezone abbreviations (e.g., AEST, AEDT) with accurate UTC offset information.

## Requirements

- [PowerToys](https://github.com/microsoft/PowerToys) installed on your system.

## Installation

### Via Command Palette

Coming soon

### Via Microsoft store

<a href="https://apps.microsoft.com/detail/9P4TC0QM648H?mode=direct">
 <img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>


### Via Winget

Coming soon

## Usage

1. Open the PowerToys Command Palette.
2. Search for Time Zone Convertor using the extension.

### Show time zones

The extension will list all time zones for the current date and time. With the local time zone being on top

![](./images/example1.png)

Supported date and time formats:
- `10:00 AM` (time only - uses current date)
- `Nov 21 2025 1:53PM` (month name format)
- `21 Nov 2025 1:53PM` (day month year format)
- `21/11/2025 1:53PM` (DMY format: MM/DD/YYYY)
- `11/21/2025 1:53PM` (MYK format: MM/DD/YYYY)
- `2025-11-21 13:53` (ISO format)

### Convert from another time zone (single source)

To convert a time from another time zone to your local time zone, you can use either of the following formats:
`<date and time>, <time zone>` or `<date and time> in <time zone>`
Examples:
- `10:00 AM, London`
- `Nov 21 2025 1:53PM, London`
- `21 Nov 2025 1:53PM in London`
- `2025-11-21 13:53 in London`

For example, to convert 10:00 AM in London to your local time zone, type:
10:00 AM, London
Or to convert a specific date and time:
Nov 21 2025 1:53PM, London
![](./images/example2.png)

### Convert to another time zone (from local time)

To convert a time to another time zone type the following
<date and time> to <time zone>
Supported date and time formats (same as above):
- `10:00 AM to London` (time only - uses current date)
- `Nov 21, 2025 1:53PM to London` (month name format)
- `21 Nov 2025 1:53PM to London` (day month year format)
- `21/11/2025 1:53PM to London` (DMY format: DD/MM/YYYY)
- `11/21/2025 1:53PM to London` (MDY format: MM/DD/YYYY)
- `2025-11-21 13:53 to London` (ISO format)

For example, to convert 10:00 AM in your local time zone to London, type:
10:00 AM to London
![](./images/example3.png)

### Convert between two arbitrary time zones

To convert a time from one time zone to another, use either of the following formats:
`<date and time>, <source time zone> to <target time zone>`
`<date and time> in<source time zone> to <target time zone>`
Examples:
- `10:00 AM, London to Tokyo`
- `Nov 21 2025 1:53PM, London to Tokyo`
- `21 Nov 2025 1:53PM in London to Tokyo`
- `2025-11-21 13:53 in London to Tokyo`

This will convert the specified date and time from the source time zone to the target time zone and display the result at the top.

For example to convert 12:30pm on the 22nd of April 2025 from Arizona to London, type:

2025-04-22 12:30pm in Arizona to London

**OR**

2025-04-22 12:30pm, Arizona to London

![](./images/example4.png)

## License

This project is licensed under the [MIT Licence](LICENCE).

## Contributing

Contributions are welcome! Feel free to submit issues or pull requests to improve this extension.

## Acknowledgments

- [PowerToys](https://github.com/microsoft/PowerToys) for providing a versatile Command Palette.
