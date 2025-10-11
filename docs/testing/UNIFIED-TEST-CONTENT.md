# Test Content Strategy - Updated

## ✅ Changes Applied

### Unified Content Strategy
All document test files (PDF, DOCX, DOC, ODT, RTF) now use **identical content** to test extraction capabilities across different formats.

### Why Same Content?
1. **Format Testing**: We're testing the extractors' ability to handle different file formats, not different content
2. **Consistency**: Same assertions can validate all formats
3. **Debugging**: If one format fails, we know it's the extractor, not content differences
4. **Simplicity**: Maintain one set of test content, not multiple variations

---

## 📄 Standard Test Content

All document files should contain:

### Page 1
```
Test Document
This is the first paragraph with some text.
Section 2
Another paragraph in section 2.
Formatted text
```

### Page 2
```
New Page
Here is content of the new page
Table
Amount Description
2021 200 This is text for 2021
2020 45.8 This not for 2021 it is for 2022
Image
```

### Table Structure
| Amount | Description |
|--------|-------------|
| 2021 | 200 | This is text for 2021 |
| 2020 | 45.8 | This not for 2021 it is for 2022 |

---

## ✅ Current Status

### PDF Tests (11 tests) - Ready
- ✅ File exists: `sample.pdf` with correct content
- ✅ Tests updated: `PdfTextExtractorTests.cs`
- ✅ Assertions check for:
  - "Test Document", "Section 2"
  - "New Page", page 2 content
  - Table: "2021", "200", "This is text for 2021"
  - Table: "2020", "45.8"
  - Multi-page with page separators

### DOCX Tests (10 tests) - Needs File Update
- ⏳ File needs update: `sample.docx` with SAME content as PDF
- ✅ Tests updated: `DocxTextExtractorTests.cs`
- ✅ Assertions check for (same as PDF):
  - "Test Document", "Section 2"
  - "New Page", page 2 content
  - Table: "2021", "200", "This is text for 2021"
  - Table: "2020", "45.8"
  - "Formatted text"
  - Line breaks between paragraphs
  - No HTML/XML formatting tags

### Plain Text Tests (11 tests) - Different Content
- ✅ Plain text files keep their own simple content
- ✅ Tests: `PlainTextExtractorTests.cs`
- Files: `sample.txt`, `sample.md`, `sample.json`, `sample.csv`

---

## 📋 Action Items

### Immediate: Create sample.docx

**Option 1: Manual (5 minutes)**
1. Open Microsoft Word or LibreOffice Writer
2. Copy the content from "Standard Test Content" above
3. Type/paste page 1 content
4. Insert page break (Ctrl+Enter)
5. Type/paste page 2 content
6. Create the 2x3 table with exact data
7. Save as `Indexer.Tests/TestData/sample.docx`

**Option 2: Copy from PDF**
1. Open `sample.pdf` in Word (File → Open)
2. Word will convert it to editable format
3. Fix any formatting issues
4. Save as `.docx`

**Option 3: I'll Generate It**
I can create a C# script using DocumentFormat.OpenXml to generate the file programmatically with exact content.

### Future: Other Formats (when extractors are implemented)

When adding support for .doc, .odt, .rtf:
- Use the same content as PDF/DOCX
- Create extractor class implementing `ITextExtractor`
- Copy existing test class and update for new format
- All assertions stay the same

---

## 🧪 Test Validation

Once `sample.docx` is created with the standard content, all tests should pass:

### PDF Tests
```bash
dotnet test --filter "FullyQualifiedName~PdfTextExtractorTests"
```
Expected: ✅ All 11 tests pass

### DOCX Tests
```bash
dotnet test --filter "FullyQualifiedName~DocxTextExtractorTests"
```
Expected: ✅ All 10 tests pass (once file is created)

### All Text Extraction Tests
```bash
dotnet test --filter "FullyQualifiedName~TextExtraction"
```
Expected: ✅ All 58 tests pass

---

## 📊 Benefits of This Approach

### For Testing
- ✅ **Isolation**: Each extractor tested independently
- ✅ **Consistency**: Same validation across all formats
- ✅ **Maintainability**: One content spec, multiple format tests
- ✅ **Debugging**: Failures clearly indicate extractor issues

### For Development
- ✅ **Template**: Easy to add new formats (copy test class, change format)
- ✅ **Verification**: Can visually compare extracted text across formats
- ✅ **Documentation**: Single source of truth for test content

### Example Test Pattern
```csharp
// Works for PDF, DOCX, DOC, ODT, RTF - just change the file!
result.Should().Contain("Test Document");
result.Should().Contain("Section 2");
result.Should().Contain("2021");
result.Should().Contain("200");
result.Should().Contain("This is text for 2021");
```

---

## 🎯 Next Steps

1. **Create `sample.docx`** - Use one of the three options above
2. **Run tests** - Verify both PDF and DOCX tests pass
3. **Document** - Update TEST-DATA-SPEC.md if needed
4. **Future formats** - When adding .doc/.odt/.rtf support, use same content

Which option would you like to use for creating the DOCX file?
