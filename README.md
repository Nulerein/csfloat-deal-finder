# CSFloat — deal finder

A local Windows app for finding listings cheaper than the CSFloat base price of an item. Comparison is done against the reference.base_price field, without any discount or premium for a specific float.

## Project status

This is an independent, unofficial project. It is not affiliated with, endorsed by, sponsored by, or otherwise connected to CSFloat or its operators.

## Running in Visual Studio Code

A Windows .NET 8 SDK is required.

1. Open this folder in Visual Studio Code.
2. Open the built-in terminal in this folder.
3. Run: dotnet run --project .\CSFloatDealFinder.csproj

You can also launch the app by double-clicking the run.bat file.

## Search setup

Each user must create and use their own CSFloat API key. Create your personal key in your CSFloat profile → Developers; this project does not provide or share API keys. Paste your key into the API key field. The app does not save the key to disk and sends it only in the request header to csfloat.com.

Specify the exact item name if needed, the price range, the minimum discount vs the base price, and the number of pages. By default, 3 pages of 50 listings are loaded; the maximum is 100 pages. Requests between pages are delayed by a pause, and already loaded results are preserved when rate limits are hit.

## How to read the results

- Price and base price are shown in USD.
- Discount is calculated from the CSFloat base price of the item, without any premium for float.
- Only valuations with a sample of at least 10 sales are used.
- Items with stickers, rare stickers or patterns, as well as Doppler, Fade, and Case Hardened variants are excluded because their special value is not reflected in the base valuation.
- Sorting allows you to show either the largest discount vs the base or the cheapest listings first.
- Click Open or double-click a listing to open it on CSFloat.

The CSFloat valuation is a market reference, not a guarantee of future resale value.

## Documentation

- CSFloat API: [official documentation](https://docs.csfloat.com/)
- Float Appraiser: [overview](https://blog.csfloat.com/introducing-the-float-appraiser/)

## License

This project is distributed under the MIT License. See the LICENSE file.