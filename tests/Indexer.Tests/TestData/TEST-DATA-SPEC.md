# Test Data File Specifications

This document specifies the exact content required in test data files to ensure proper test validation.

## Common Content for Document Files

**All document test files (PDF, DOCX, DOC, ODT, RTF) use the SAME content** to test extraction capability across formats.

### Page 1 Content
```
Test Document
This is the first paragraph with some text.
Section 2
Another paragraph in section 2.
Formatted text
```

### Page 2 Content
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
```
┌────────┬─────────────┬──────────────────────────────────────┐
│ Amount │ Description │                                      │
├────────┼─────────────┼──────────────────────────────────────┤
│ 2021   │ 200         │ This is text for 2021                │
├────────┼─────────────┼──────────────────────────────────────┤
│ 2020   │ 45.8        │ This not for 2021 it is for 2022     │
└────────┴─────────────┴──────────────────────────────────────┘
```

---

## sample.pdf (PDF Document)

**Status**: ✅ File provided by user with actual content

**Test Coverage**: ✅ Fully tested in `PdfTextExtractorTests.cs`

**Test Assertions**:
- ✅ Contains "Test Document" (page 1 title)
- ✅ Contains "This is the first paragraph with some text" (page 1 content)
- ✅ Contains "Section 2" (section header)
- ✅ Contains "Another paragraph in section 2" (section content)
- ✅ Contains "New Page" (page 2 header)
- ✅ Contains "Here is content of the new page" (page 2 content)
- ✅ Contains "Table", "Amount", "Description" (table headers)
- ✅ Contains "2021", "200", "This is text for 2021" (table row 1)
- ✅ Contains "2020", "45.8" (table row 2)
- ✅ Contains "\n\n" (page separators)
- ✅ Length > 100 characters
- ✅ Deterministic extraction

---

## sample.docx (Word Document - Office Open XML)

**Status**: ⏳ Needs to be created with SAME content as PDF

**Test Coverage**: ✅ Fully tested in `DocxTextExtractorTests.cs`

**How to Create**:
1. Open Microsoft Word or LibreOffice Writer
2. Create document with content matching PDF (see "Page 1 Content" and "Page 2 Content" above)
3. Insert page break after "Formatted text"
4. Create 2x3 table on page 2 with the exact data shown above
5. Apply formatting (bold for headers, etc.)
6. Save as `.docx` format

**Test Assertions** (same as PDF):
- ✅ Contains "Test Document" 
- ✅ Contains "This is the first paragraph with some text"
- ✅ Contains "Section 2"
- ✅ Contains "Another paragraph in section 2"
- ✅ Contains "New Page" (page 2)
- ✅ Contains "Here is content of the new page"
- ✅ Contains "Table", "Amount", "Description"
- ✅ Contains "2021", "200", "This is text for 2021"
- ✅ Contains "2020", "45.8"
- ✅ Contains "Formatted text"
- ✅ Has newlines between paragraphs
- ✅ Length > 100 characters
- ✅ No HTML/formatting tags in extracted text
- ✅ Deterministic extraction

---

## sample.doc (Word Document - Legacy Binary Format)

**Status**: ⏳ Future - extractor not yet implemented
**Content**: Should match PDF when extractor is added
**Notes**: Requires different library (e.g., Aspose.Words or NPOI) for legacy .doc format

---

## sample.odt (OpenDocument Text)

**Status**: ⏳ Future - extractor not yet implemented  
**Content**: Should match PDF when extractor is added
**Notes**: Requires OpenDocument library for ODT support

---

## sample.rtf (Rich Text Format)

**Status**: ⏳ Future - extractor not yet implemented
**Content**: Should match PDF when extractor is added
**Notes**: Requires RTF parser library

---

## Content Differentiation Strategy

**DOCX Focus**: Document processing, file format handling, text extraction  
**PDF Focus**: Vector search, database architecture, query processing

**Why Different Content**:
1. **Test Isolation**: Ensures we're testing the right extractor
2. **Format Validation**: Verifies format-specific features work
3. **Debugging**: Failed tests clearly indicate which file/extractor is broken
4. **Real-World**: Different document types typically have different content

---

## Plain Text Files (for reference)

### sample.txt
```
This is sample plain text content.
It has multiple lines for testing text extraction.
Line three with special chars: é, ñ, 中文
```

### sample.md
```
# Sample Markdown

This file tests **markdown** format extraction.

## Features
- List item 1
- List item 2
```

### sample.json
```json
{
  "name": "test document",
  "type": "json",
  "value": 123
}
```

### sample.csv
```
name,age,city
John,30,NYC
Jane,25,LA
```

---

## Verification Checklist

Before committing test files, verify:

- [ ] DOCX contains "Executive Summary" and "OpenXML SDK"
- [ ] PDF contains "Vector Search Architecture" and "PdfPig library"
- [ ] DOCX and PDF have NO overlapping key phrases
- [ ] Both files have 2 pages
- [ ] Both files contain tables with proper structure
- [ ] Both files are > 500 characters when extracted
- [ ] Both files have multiple paragraphs with line breaks
- [ ] All plain text files have expected content per spec above
