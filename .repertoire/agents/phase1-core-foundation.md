# Phase 1: Core Foundation & Inverted Dependency Cleanup Report

## Goal
Establish foundational single-ground utilities and linguistic engines in `Core/`, eliminating data and presentation layer contamination, breaking inverted layer dependencies so `Core/` has zero dependencies on `Infrastructure/`, and updating `UNITS.md`.

## Actions Taken
1. **Created `Core/Utilities/StringNormalization.cs`**:
   - Single ground for Arabic string normalization, ASCII digit conversion (`ToAsciiDigits`), diacritics stripping (`StripDiacritics`), orthographic label folding (`NormalizeLabel`), and HTML cleaning (`CleanHtml`).
   - Refactored `Infrastructure/Http/Parsers/StructuralExtractor.cs` to delegate `Normalize`, `ToAsciiDigits`, and `NormalizeLabel` to `StringNormalization`.
2. **Eliminated Inverted Dependencies**:
   - Refactored `Core/Formatting/ArabicProposalParser.cs` to remove references to `Infrastructure/Http/Parsers`.
   - Verified that `Core/` has 0 references or `using` directives pointing to `Infrastructure/`.
3. **Implemented Canonical Formatters**:
   - `Core/Formatting/ArabicNameFormatter.cs`: Implemented pure "ال" prefix stripping (`StripArticle`), first letter extraction (`GetFirstLetter`), and avatar initials extraction (`GetInitials`).
   - `Core/Formatting/ArabicProposalParser.cs`: Added canonical Arabic proposal count pluralization (`Format`: "0 عرض", "عرض واحد", "عرضان", "3-10 عروض", "11+ عرضاً").
   - `Core/Formatting/TextTruncator.cs`: Added single ground for string truncation and word-boundary truncation with ellipsis (`Truncate`, `TruncateWords`).
   - `Core/Formatting/PipelineTelemetryFormatter.cs`: Implemented centralized formatting for worker states ("يعالج", "مكتمل", "خطأ", "خامل") and elapsed durations ("12.4 ث").
   - `Core/Formatting/ArabicRelativeTime.cs`: Extended with `ParseRelativeNumber(string? text)` to unify scraper relative time parsing across listing and detail parsers.
4. **Enhanced Test Suite**:
   - Updated `tools/ParserTests/ParserTests.csproj` to link all new Core formatting and utility source files.
   - Added unit test cases for `ArabicNameFormatter`, `ArabicProposalParser.Format`, `TextTruncator`, `PipelineTelemetryFormatter`, and `ArabicRelativeTime.ParseRelativeNumber`.
5. **Documentation**:
   - Updated `UNITS.md` with catalog rows under "Core helpers".

## Files Touched
- `Core/Utilities/StringNormalization.cs` (Created)
- `Core/Formatting/ArabicNameFormatter.cs` (Created)
- `Core/Formatting/ArabicProposalParser.cs` (Updated)
- `Core/Formatting/ArabicRelativeTime.cs` (Updated)
- `Core/Formatting/PipelineTelemetryFormatter.cs` (Created)
- `Core/Formatting/TextTruncator.cs` (Created)
- `Infrastructure/Http/Parsers/StructuralExtractor.cs` (Updated)
- `tools/ParserTests/ParserTests.csproj` (Updated)
- `tools/ParserTests/Program.cs` (Updated)
- `UNITS.md` (Updated)

## Verification
- Headless test suite: `dotnet run --project tools\ParserTests` -> 133 tests passed, 0 failed.
- Windows compilation: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Debug` -> 0 errors, 0 warnings.
