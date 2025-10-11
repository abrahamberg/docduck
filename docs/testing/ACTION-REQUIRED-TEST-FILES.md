# Action Required: Update Test Data Files

## Summary

The test files `sample.docx` and `sample.pdf` need to be updated with **specific, distinct content** to enable meaningful test validation. Currently, tests only check for generic properties (length > 50 characters, has newlines). With the updated tests, we need specific content.

## Updated Tests Now Verify

### DOCX Tests Check For:
✅ Contains "Executive Summary" (section header from page 1)  
✅ Contains "DOCX extraction capabilities" (paragraph text)  
✅ Contains "OpenXML SDK" (list item - technology stack)  
✅ Contains "Implementation Details" (page 2 header)  
✅ Contains "chunking service" (page 2 content)  
✅ Contains "DOCX", "Supported", "High" (table cells)  
✅ Has newlines between paragraphs  
✅ Length > 500 characters  
✅ Does NOT contain "Vector Search Architecture" (proves it's not PDF)  
✅ Does NOT contain "PdfPig library" (proves it's not PDF)  

### PDF Tests Check For:
✅ Contains "Vector Search Architecture" (title - PDF specific)  
✅ Contains "PdfPig library" (PDF-specific technology)  
✅ Contains "pgvector extension" (paragraph content)  
✅ Contains "System Architecture" (page 1 section)  
✅ Contains "Search Implementation" (page 2 header)  
✅ Contains "cosine similarity" (page 2 content)  
✅ Contains "Component Overview", "Indexer Service", "PostgreSQL 15" (table)  
✅ Has "\n\n" page separators  
✅ Length > 600 characters  
✅ Does NOT contain "Executive Summary" (proves it's not DOCX)  
✅ Does NOT contain "OpenXML SDK" (proves it's not DOCX)  

## How to Create the Files

### Option 1: Manual Creation (Recommended for Quality)

**For sample.docx:**
1. Open Microsoft Word or LibreOffice Writer
2. Copy the content from `TEST-DATA-SPEC.md` under "sample.docx" section
3. Add a page break after the "Technology Stack" section
4. Format with:
   - Bold headings
   - Create the 3x3 table for features
   - Bullet list for technology stack
5. Save as `Indexer.Tests/TestData/sample.docx`

**For sample.pdf:**
1. Open Microsoft Word or LibreOffice Writer  
2. Copy the content from `TEST-DATA-SPEC.md` under "sample.pdf" section
3. Add a page break after the "Database Schema" section
4. Format with:
   - Bold headings
   - Create the 3x3 table for components
   - Numbered list for query pipeline
5. Export/Save as PDF to `Indexer.Tests/TestData/sample.pdf`

### Option 2: Generate Programmatically

I can create a helper script using DocumentFormat.OpenXml and iText7/PdfSharp to generate these files with exact content. Would you like me to create that?

### Option 3: Use Existing Files (If Available)

If you already have these files with real content, we can:
1. Extract the actual text from them
2. Update the test assertions to match the real content
3. Document the content in TEST-DATA-SPEC.md

## Why This Matters

**Before (Current State):**
```csharp
result.Length.Should().BeGreaterThan(50);  // Generic, could pass with wrong file
```

**After (Improved State):**
```csharp
result.Should().Contain("Executive Summary");  // Specific to DOCX content
result.Should().NotContain("Vector Search");    // Proves it's not PDF
```

**Benefits:**
1. **Test Isolation**: Failures clearly indicate which extractor is broken
2. **Content Validation**: Ensures we're extracting the *right* text, not just *some* text
3. **Regression Detection**: Changes to extraction logic are immediately caught
4. **Debugging**: Test failures provide exact context about what's missing

## Current Status

✅ Tests updated with specific assertions  
✅ Specification document created (TEST-DATA-SPEC.md)  
⏳ **ACTION NEEDED**: Create/update sample.docx with specified content  
⏳ **ACTION NEEDED**: Create/update sample.pdf with specified content  

## Next Steps

**Choose one:**

1. **I'll create them manually** - Use the spec in TEST-DATA-SPEC.md as a guide
2. **Generate them for me** - I'll create a C# script to generate both files programmatically
3. **Use what I have** - Let me know and I'll update tests to match your existing files

Which option would you prefer?
