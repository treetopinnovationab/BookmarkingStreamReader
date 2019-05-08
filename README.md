# Bookmarking Stream Reader

Files are easy to read with the .NET BCL StreamReader, but only once or all-at-once. If you read one line at a time whenever a file has changed and wish to resume at a known position, StreamReader's buffer and other factors prevent you from knowing the exact position. Since the line-reading operations also chop off the line ending, it's hard to know if you've read an incomplete line, or which line ending is being used. There are also other factors - see "The gory details" below.

Bookmarking Stream Reader is a fork of the StreamReaders from the .NET BCL, extended with the necessary tracking to provide "bookmarks", which can be used to seek the stream to the right position and continue reading, assuming the beginning of the file hasn't changed.

Like the source material, it is available under the MIT license.

## NuGet package

...is not available yet. For now, clone the repo/download the current source.


## Supported frameworks

We provide a version for .NET Core 2.2, based on the StreamReader from .NET Core 2.2, and a version for .NET Framework 4.0 and above, based on the StreamReader from .NET Framework 4.7.2.

## What's inside

Bookmarking Stream Reader provides:

  * A **LineBookmark** struct, noting the number of bytes and `char`s already read.
  * A **BookmarkingStreamReader** class, providing:
    * A **ReadDetailedLine** method, returning detailed information about the line, including:
      * the line break characters used, if any (supported: none/EOF, `\r` (CR), `\r\n` (CRLF), `\n` (LF))
      * a LineBookmark for the position before reading the line, suitable for resuming at the point before the line was read, to re-read the line
      * a LineBookmark for the position after reading the line including line break, suitable for resuming at the point after the line was read, to read the next line
      * the text, with and without the line break characters
    * A **ResumeFromBookmark** method to seek to the position of a LineBookmark and use its character index.
    * A **ResumeFromBeginning** method to seek to the beginning of a stream and dump all character tracking information.
      * Character tracking implementation for all single-byte encodings (including ASCII, Windows Latin-1 and ISO 8859-1) as well as UTF-8 and UTF-16. This logic works out which `char` offset corresponds to which byte index in the buffer, which along with knowing the previous number of `char`s and bytes provide absolute offsets.

## Sample usage

```csharp
// Dispose and error handling omitted

// file contents:
// abcdef\r\n
// xyzzy\n
// foobar

const string pathToFile = ...;

// Read the first line

var bsr = new BookmarkingStreamReader(new FileStream(pathToFile), Encoding.UTF8);

var firstLine = bsr.ReadDetailedLine().Value;
firstLine.TextWithoutLineEnding // => "abcdef"
firstLine.TextWithLineEnding // => "abcdef\r\n"
firstLine.BookmarkingLineEnding // => BookmarkingLineEnding.CarriageReturnLineFeed

// Create a bookmark for resuming at the current position
var bookmarkForNextLine = firstLine.MakeBookmarkForReadingNextLine();


// Create a new reader and resume it at the position.
// (Take care not to reuse the streams! Each stream has its own position.)
var bsr2 = new BookmarkingStreamReader(new FileStream(pathToFile), Encoding.UTF8);
bsr2.ResumeFromBookmark(bookmarkForNextLine);

var secondLine = bsr2.ReadDetailedLine().Value;
secondLine.TextWithoutLineEnding // => "xyzzy"
secondLine.TextWithLineEnding // => "xyzzy\n"
secondLine.BookmarkingLineEnding // => BookmarkingLineEnding.LineFeed

var finalLine = bsr2.ReadDetailedLine().Value;
finalLine.TextWithoutLineEnding // => "foobar"
finalLine.TextWithLineEnding // => "foobar"
finalLine.BookmarkingLineEnding // => BookmarkingLineEnding.None

// Like ReadLine(), null signals end of file (nothing more left to read)
var lineAfterFinalLine = bsr2.ReadDetailedLine();

lineAfterFinalLine == null // => true


// Create a bookmark for resuming at the beginning of the final line.
// You may want to re-read a line if you expect partial data:
// - it didn't have the expected data
// - it didn't have a line ending and it should have
// - it had \r when you expected \r\n 
// etc...

var bookmarkForReReadingFinalLine = finalLine.MakeBookmarkForReReadingLine();

var bsr3 = new BookmarkingStreamReader(new FileStream(pathToFile), Encoding.UTF8);
bsr3.ResumeFromBookmark(bookmarkForReReadingFinalLine);

var finalLineAgain = bsr3.ReadDetailedLine().Value;
finalLineAgain.TextWithoutLineEnding // => "foobar"
finalLineAgain.TextWithLineEnding // => "foobar"
finalLineAgain.BookmarkingLineEnding // => BookmarkingLineEnding.None
```

## Status

The reader is under development. Regular use with files without invalid contents (like invalid multi-byte sequences) should work fine.

Already done:
  * Making sure split characters, where a multi-byte sequence straddles a buffer boundary, does not confuse or offset the tracking. (The bookmark should never point into the middle of a split character.)
  * Making sure BOM (byte order marks) are handled coherently.
  * Hiding incompatible or unimplemented methods. You can't Read from the reader other than through ReadDetailedLine, because of the extra tracking that needs to happen when the buffer is reinitialized. Peek is potentially harmless but is hidden for consistency.
  * Character tracking information for UTF-16.

Next up:
  * Testing all invariants.
  * Provide NuGet package.
  * Making sure recovery from invalid characters in the underlying Encoding instance doesn't desync.


In the future, we may want to provide:
  * An implementation of the asynchronous methods.
  * Transparent support for grabbing a few bytes around the beginning of the bookmark and/or the beginning of the file to validate having seeked to the right position, in case the file was rewritten to truncate information at the beginning.
  * Transparent detection of something else having seeked the stream in the background and throwing an exception. Keeping track of the character index requires seeking the stream from the beginning or resuming from a bookmark with this information.
  * Possibly character tracking information for other multi-byte encodings.
  * Reading other elements than lines.
  * Reading in reverse.


## The gory details - "why is this so hard?"

This is a deceptive problem. It looks very simple, but is made hard by several interacting things.

### Buffering

The StreamReader doesn't read directly from the file. It fills a buffer with chunks of bytes to prevent reading everything directly from disk. When asked to read a line, it reads bytes from this buffer, and re-fills the buffer when necessary.

The stream's position, which is publicly visible, is the position of the end of the buffer, but *you don't know where the text you receive from the reader is in this buffer*, so you can't work out the position in the stream. If the buffer is 1024 bytes big, you could read 10 short lines from within the same buffer, without the stream's position moving. And if you have a long line, it could require re-filling the buffer several times.

Since the buffer is an implementation detail of the StreamReader, it does not track where the just-read text was positioned in the buffer, and there's no API to provide this information. Bookmarking Stream Reader adds this tracking.

### Line breaks

StreamReader's ReadLine also chops off the line breaking characters, if present. Did you read `\r`, `\n`, `\r\n` or just encounter the end of the file? No idea - but it affects the current position. Bookmarking Stream Reader keeps track of this too.

### Chars and bytes

In order to have text instead of bytes, StreamReader converts the buffers of bytes to buffers of `char` (System.Char), which are UTF-16 code points, which will not align with the byte offset even for UTF-16-encoded text (since bytes are 8-bit integers and UTF-16 code points are 16-bit integers). Keeping track of the last read position requires tracking the byte offset across the actual encoding in sync with the buffer being filled.

For example, the string `AZ✨💩123` stored on disk as UTF-8 consists of:

                 character: ---A--- ---Z--- ---✨--------- ---💩--------- ---1--- ---2--- ---3---
         Unicode character:  U+0041  U+005A   U+2728        U+1F4A9         U+0031  U+0032  U+0033
                    (index)       0       1        2              3              4       5       6
      
    (A) UTF-16 code points:    0041    005A     2728           D83D DCA9      0031    0032    0033
                    (index)       0       1        2              3    4         5       6       7
      
              UTF-16 bytes:   00 41   00 5A    27 28          D8 3D DC A9    00 31   00 32   00 33
                    (index)    0  1    2  3     4  5           6  7  8  9    10 11   12 13   14 15
    
     (B) UTF-8 code points:      41      5A       E2 9C A8    F0 9F 92 A9       31      32      33
         and bytes  (index)       0       1        2  3  4     5  6  7  8        9      10      11


Being able to resume at point X requires knowing not only the position in the stream of bytes ((B) in this case), but also the number of previous characters, the position in the stream of UTF-16 code points, (A).

Bookmarking Stream Reader keeps enough information to solve this. Among other things, this involves going through the byte/character buffers by the stream reader in order to work out which code points have been read. In the code, this is referred to as working out the "byte advancement info". For single byte encodings, 1 char that could be read from that file will always equal 1 byte*, while for UTF-8 and UTF-16, it depends on the data. With this information, the number of bytes and the number of chars seen in the file up to this point can be kept, and this is the information in the bookmark.

(\* given that no value represents a Unicode character not representable in a single UTF-16 code point; in our testing, this holds for all values in all single-byte encodings supported by .NET Core with System.Text.Encoding.CodePages and .NET Framework)

### This is not a criticism of StreamReader

StreamReader is a good piece of code that does what it's supposed to do. It just can't currently do what Bookmarking Stream Reader can, likely because there's no good API in the Encoding class to build the byte advancement info mentioned above and provide the byte-to-char mapping.


## Why a .NET Framework 4 version?

.NET Core's requirements exclude some of the Windows Server versions we want to support. If you can use .NET Core, use .NET Core.