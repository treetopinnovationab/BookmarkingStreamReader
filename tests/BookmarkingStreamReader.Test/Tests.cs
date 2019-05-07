using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Bookmarking.Test {

    public class BookmarkingStreamReaderTests {
        private static byte[] GetBytes(string str) {
            var bytesInText = str.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[bytesInText.Length];
            for (int i = 0; i < bytesInText.Length; i++) {
                bytes[i] = byte.Parse(bytesInText[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return bytes;
        }

        class BytesBuilder : IEnumerable<byte> { // IEnumerable<T> required for collection initializers
            private readonly List<byte[]> _bytes = new List<byte[]>();
            public void Add(byte b) {
                _bytes.Add(new[] { b });
                _finalCap++;
            }

            public void Add(params byte[] bytes) {
                _bytes.Add(bytes);
                _finalCap += bytes.Length;
            }

            private int _finalCap = 0;

            public byte[] ToArray() {
                var output = new byte[_finalCap];
                var i = 0;
                foreach (var seq in _bytes) {
                    var len = seq.Length;
                    Array.Copy(seq, 0, output, i, len);
                    i += len;
                }

                return output;
            }

            public IEnumerator<byte> GetEnumerator() {
                foreach (var b in _bytes) {
                    foreach (var by in b) {
                        yield return by;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }
        }

        private static readonly byte[] _byteOrderMark = new byte[] { 0xEF, 0xBB, 0xBF };
        private static readonly byte _newline = 0x0A;

        private static readonly UTF8Encoding _u8NoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static byte[] UTF8(string str) {
            return _u8NoBOM.GetBytes(str);
        }

        /// <summary>
        /// The example usage from the readme file.
        /// </summary>
        [Fact]
        public void SimpleExampleFromReadme() {
            var utf8Contents = UTF8("abcdef\r\nxyzzy\nfoobar");
            var memoryStreamCreator = new Func<MemoryStream>(() => new MemoryStream(utf8Contents));

            var bsr = new BookmarkingStreamReader(memoryStreamCreator(), Encoding.UTF8);
            var firstLine = bsr.ReadDetailedLine().Value;

            Assert.Equal("abcdef", firstLine.TextWithoutLineEnding);
            Assert.Equal("abcdef\r\n", firstLine.TextWithLineEnding);
            Assert.Equal(BookmarkingLineEnding.CarriageReturnLineFeed, firstLine.BookmarkingLineEnding);

            // Create a bookmark for resuming at the current position
            var bookmarkForNextLine = firstLine.MakeBookmarkForReadingNextLine();


            // Create a new reader and resume it at the position.
            // (Take care not to reuse the streams! Each stream has its own position.)
            var bsr2 = new BookmarkingStreamReader(memoryStreamCreator(), Encoding.UTF8);
            bsr2.ResumeFromBookmark(bookmarkForNextLine);


            var secondLine = bsr2.ReadDetailedLine().Value;

            Assert.Equal("xyzzy", secondLine.TextWithoutLineEnding);
            Assert.Equal("xyzzy\n", secondLine.TextWithLineEnding);
            Assert.Equal(BookmarkingLineEnding.LineFeed, secondLine.BookmarkingLineEnding);

            var finalLine = bsr2.ReadDetailedLine().Value;

            Assert.Equal("foobar", finalLine.TextWithoutLineEnding);
            Assert.Equal("foobar", finalLine.TextWithLineEnding);
            Assert.Equal(BookmarkingLineEnding.None, finalLine.BookmarkingLineEnding);

            // Like ReadLine(), null signals end of stream

            var lineAfterFinalLine = bsr2.ReadDetailedLine();

            Assert.Null(lineAfterFinalLine);


            // Create a bookmark for resuming at the beginning of the final line.
            // You may want to re-read a line if it didn't have the expected data,
            // if it didn't have a line ending and it should have,
            // if it had \r when you expected \r\n, etc...

            var bookmarkForReReadingFinalLine = finalLine.MakeBookmarkForReReadingLine();

            var bsr3 = new BookmarkingStreamReader(memoryStreamCreator(), Encoding.UTF8);
            bsr3.ResumeFromBookmark(bookmarkForReReadingFinalLine);

            var finalLineAgain = bsr3.ReadDetailedLine().Value;
            Assert.Equal("foobar", finalLineAgain.TextWithoutLineEnding);
            Assert.Equal("foobar", finalLineAgain.TextWithLineEnding);
            Assert.Equal(BookmarkingLineEnding.None, finalLineAgain.BookmarkingLineEnding);
        }

        /// <summary>
        /// Check that the Byte Order Mark does not affect the character index.
        /// </summary>
        [Fact]
        public void HandleBOMCorrectlyWithNewline() {
            var testChar = "Z";
            var bomThenZ = new BytesBuilder { _byteOrderMark, UTF8(testChar), _newline }.ToArray();

            var memoryStream = new MemoryStream(bomThenZ);

            var bookm = new BookmarkingStreamReader(memoryStream, Encoding.UTF8);
            var lineMaybe = bookm.ReadDetailedLine();
            var line = lineMaybe.Value;
            Assert.Equal(expected: "Z", actual: line.TextWithoutLineEnding);
            Assert.Equal(expected: BookmarkingLineEnding.LineFeed, actual: line.BookmarkingLineEnding);
            var readNextBookmark = line.MakeBookmarkForReadingNextLine();
            Assert.Equal(expected: 1, actual: readNextBookmark.CharIndex);
            Assert.Equal(expected: 4, actual: readNextBookmark.Position);
            var rereadBookmark = line.MakeBookmarkForReReadingLine();
            Assert.Equal(expected: -1, actual: rereadBookmark.CharIndex);
        }

        /// <summary>
        /// Check that the Byte Order Mark does not affect the character index.
        /// </summary>
        [Fact]
        public void HandleBOMCorrectlyWithoutNewline() {
            var testChar = "Z";
            var bomThenZ = new BytesBuilder { _byteOrderMark, UTF8(testChar) }.ToArray();

            var memoryStream = new MemoryStream(bomThenZ);

            var bookm = new BookmarkingStreamReader(memoryStream, Encoding.UTF8);
            var lineMaybe = bookm.ReadDetailedLine();
            var line = lineMaybe.Value;
            Assert.Equal(expected: testChar, actual: line.TextWithoutLineEnding);
            Assert.Equal(expected: BookmarkingLineEnding.None, actual: line.BookmarkingLineEnding);
            var readNextBookmark = line.MakeBookmarkForReadingNextLine();
            Assert.Equal(expected: 0, actual: readNextBookmark.CharIndex);
            Assert.Equal(expected: 3, actual: readNextBookmark.Position);
            var rereadBookmark = line.MakeBookmarkForReReadingLine();
            Assert.Equal(expected: -1, actual: rereadBookmark.CharIndex);
        }

        private static byte[] Repeat(byte[] r, int repetitions) {
            var b = new byte[r.Length * repetitions];
            for (int i = 0; i < repetitions; i++) {
                Array.Copy(r, 0, b, r.Length * i, r.Length);
            }
            return b;
        }

        private static string Repeat(string r, int repetitions) {
            var sb = new StringBuilder(r.Length * repetitions);
            for (int i = 0; i < repetitions; i++) {
                sb.Append(r);
            }
            return sb.ToString();
        }

        /// <summary>
        /// This is just a playground test.
        /// </summary>
        [Fact]
        public void TestAlignment() {
            return;
            var singleByteChar = "_";
            var singleByteChar2 = "-";
            var bytesBuilder = new BytesBuilder();
            var doubleByteChar = "ä";

            const int bufferSize = 128;

            bytesBuilder.Add(Repeat(UTF8(singleByteChar), 63));
            bytesBuilder.Add(_newline);
            bytesBuilder.Add(Repeat(UTF8(singleByteChar), 63));
            bytesBuilder.Add(UTF8(doubleByteChar));
            bytesBuilder.Add(Repeat(UTF8(singleByteChar2), 63));
            bytesBuilder.Add(_newline);
            bytesBuilder.Add(Repeat(UTF8(singleByteChar2), 63));

            var bytes = bytesBuilder.ToArray();

            var textInfo = DumpTextInfo(bytes, Encoding.UTF8);

            var memoryStream = new MemoryStream(bytes);

            var bookm = new BookmarkingStreamReader(memoryStream, Encoding.UTF8, false, bufferSize);
            bookm.LocallyTrackDebug();
            var line = bookm.ReadDetailedLine();
            var line2 = bookm.ReadDetailedLine();
            var beforeLine2Bookmark = line2.Value.MakeBookmarkForReReadingLine();
            var afterLine2Bookmark = line2.Value.MakeBookmarkForReadingNextLine();

            var line3 = bookm.ReadDetailedLine();
            var debug = bookm.LocalDebugText;

            var ms2 = new MemoryStream(bytes);
            var bookm2 = new BookmarkingStreamReader(ms2, Encoding.UTF8, false, bufferSize);
            bookm2.LocallyTrackDebug();
            bookm2.ResumeFromBookmark(beforeLine2Bookmark);
            var line2ReRead = bookm2.ReadDetailedLine();
            var debug2 = bookm2.LocalDebugText;


            var ms3 = new MemoryStream(bytes);
            var bookm3 = new BookmarkingStreamReader(ms3, Encoding.UTF8, false, bufferSize);
            bookm3.LocallyTrackDebug();
            bookm3.ResumeFromBookmark(afterLine2Bookmark);
            var line3ReRead = bookm3.ReadDetailedLine();
            var debug3 = bookm3.LocalDebugText;
        }


        private static string DumpTextInfo(byte[] bytes, Encoding encoding) {
            var chars = encoding.GetChars(bytes);
            var sb = new StringBuilder();

            sb.AppendLine($"bytes in {encoding.EncodingName} form:");
            var idx = 0;
            foreach (var b in bytes) {
                sb.AppendLine($"{idx:x4} {idx.ToString().PadLeft(4, '0')}  {b.ToString().PadLeft(3, ' ')}  0x{b:X2}");
                idx++;
            }

            sb.AppendLine($"UTF-16 chars:");
            idx = 0;
            foreach (var ch in chars) {
                var d = (int)ch;
                sb.AppendLine($"{idx:x4} {idx.ToString().PadLeft(4, '0')}  {d.ToString().PadLeft(5, ' ')}  0x{d:X4}");
                idx++;
            }

            sb.AppendLine($"Unicode code points:");
            var str = new string(chars);
            var codePointC = 0;
            for (int i = 0; i < str.Length; i++) {
                var utf32 = char.ConvertToUtf32(str, i);
                if (char.IsSurrogate(str, i)) {
                    i++;
                }
                sb.AppendLine($"{codePointC:x4} {codePointC.ToString().PadLeft(4, '0')}  {utf32.ToString().PadLeft(8, ' ')}  U+{utf32:X6}");
                codePointC++;
            }

            return sb.ToString();
        }

        class MemoryStreamAppender {
            public MemoryStream MemoryStream { get; } = new MemoryStream();

            public void AppendButMaintainPosition(params byte[] bytes) {
                var existingPosition = MemoryStream.Position;
                var exLength = MemoryStream.Length;
                MemoryStream.Seek(exLength, SeekOrigin.Begin);
                MemoryStream.Write(bytes, 0, bytes.Length);
                MemoryStream.Seek(existingPosition, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// This test checks that an incomplete line can be read incrementally. The "end-of-line" bookmark should read new information as it is written.
        /// </summary>
        [Fact]
        public void HandleIncrementalReading() {
            var testChar = "😀";
            var simpleChar = "A";
            var bytesBuilder = new BytesBuilder();
            bytesBuilder.Add(UTF8(simpleChar));
            var initialBytes = bytesBuilder.ToArray();

            var memoryStreamAppender = new MemoryStreamAppender();
            memoryStreamAppender.AppendButMaintainPosition(initialBytes);

            const int bufferSize = 128;

            var bookm = new BookmarkingStreamReader(memoryStreamAppender.MemoryStream, Encoding.UTF8, false, bufferSize);
            bookm.LocallyTrackDebug();

            var incompleteLine = bookm.ReadDetailedLine();

            Assert.NotNull(incompleteLine);
            Assert.Equal(incompleteLine.Value.TextWithLineEnding, simpleChar);

            var startBookmark = incompleteLine.Value.MakeBookmarkForReReadingLine();

            var atEndOfIncompleteLine = incompleteLine.Value.MakeBookmarkForReadingNextLine();

            var additionalBytes = UTF8(testChar);
            memoryStreamAppender.AppendButMaintainPosition(additionalBytes);

            bookm.ResumeFromBookmark(atEndOfIncompleteLine);
            var lessIncompleteLine = bookm.ReadDetailedLine();
            Assert.NotNull(lessIncompleteLine);
            Assert.Equal(lessIncompleteLine.Value.TextWithLineEnding, testChar);

            memoryStreamAppender.AppendButMaintainPosition(_newline);

            bookm.ResumeFromBookmark(startBookmark);
            var completeLine = bookm.ReadDetailedLine();
            Assert.NotNull(completeLine);
            Assert.Equal(completeLine.Value.TextWithLineEnding, simpleChar + testChar + "\n");
        }


        /// <summary>
        /// When using UTF-8 or other multi-byte encodings, there's always the risk that a multi-byte sequence
        /// will straddle the boundaries of the stream reader's buffer.
        ///
        /// For example: | A1 A2 A3 B1 |. When reading the next part, the buffer will be | B2 B3 C \n |.
        /// So, the reader needs to be aware that a character (B) can have started before the current buffer,
        /// and that the character does not necessarily end in the same buffer.
        ///
        /// This test creates a large amount of text with cycle of lines, where each line is filled with
        /// [one, two, three, etc up to numRepetitionsCycle] smiling face emoji (U+1F600), a character in the
        /// Supplementary Multilingual Plane, and which takes up multiple bytes.
        /// It also uses a small buffer size, and starts multiple readers on this text.
        ///
        /// First, there's the simple reader, which reads each line in turn, as usual.
        ///
        /// s1
        ///    s2
        ///       s3
        ///          s4
        ///             ...
        ///
        /// After this reader is done with each line, it takes a bookmark, and it creates a reader which resumes
        /// from this bookmark and starts reading.
        ///
        /// s1
        /// r2 s2
        ///    r3 s3
        ///       r4 s4
        ///          r5 ...
        ///
        /// Every previously started "resume reader" also continues reading from that point on until
        /// the rest of the file.
        ///
        /// s1
        /// r2 s2
        /// .3 r3 s3
        /// .4 .4 r4 s4
        /// .5 .5 .5 r5 ...
        /// .. .. .. .. ...
        ///
        /// This should exercise most cases the stream reader will see in the wild.
        /// </summary>
        [Fact]
        public void HandlePossibleMultiByteMisalignment() {

            const int numRepetitionsCycle = 77;

            int NumRepetitions(int count) {
                return (count % numRepetitionsCycle);
            }

            const bool debugThisTest = false;


            var testChar = "😀";
            var bytesBuilder = new BytesBuilder();
            for (int i = 1; i < 240; i++) {
                var toRepeat = NumRepetitions(i);
                bytesBuilder.Add(Repeat(UTF8(testChar), toRepeat));
                bytesBuilder.Add(_newline);
            }
            var testsZ = bytesBuilder.ToArray();

            if (debugThisTest) {
                var testInfo = DumpTextInfo(testsZ, Encoding.UTF8);
            }

            var memoryStream = new MemoryStream(testsZ);

            const int bufferSize = 128;

            var bookm = new BookmarkingStreamReader(memoryStream, Encoding.UTF8, false, bufferSize);
            if (debugThisTest) {
                bookm.LocallyTrackDebug();
            }

            var previousResumedStreamReaders = new List<Tuple<BookmarkingStreamReader, int>>();



            var expectNum = 1;
            do {
                //var preReadDebugState = bookm.DebugState();
                var lineMaybe = bookm.ReadDetailedLine();
                //var postReadDebugState = bookm.DebugState();
                if (lineMaybe == null) {
                    break;
                }
                var line = lineMaybe.Value;
                var expectedLine = Repeat(testChar, NumRepetitions(expectNum));
                Assert.Equal(expected: expectedLine, actual: line.TextWithoutLineEnding);
                Assert.Equal(expected: BookmarkingLineEnding.LineFeed, actual: line.BookmarkingLineEnding);
                var readSameBookmark = line.MakeBookmarkForReReadingLine();

                foreach (var previousT in previousResumedStreamReaders) {
                    var previous = previousT.Item1;
                    var startedNum = previousT.Item2;
                    //var previousRePreReadDebugState = previous.DebugState();
                    var linePreviousReReadMaybe = previous.ReadDetailedLine();
                    //var previousRePostReadDebugState = previous.DebugState();
                    Assert.NotNull(linePreviousReReadMaybe);

                    var linePreviousReRead = linePreviousReReadMaybe.Value;
                    if (line.PositionAfterLineEnding != linePreviousReRead.PositionAfterLineEnding
                        || line.StartPosition != linePreviousReRead.StartPosition) {
                        var bp = 42;
                    }
                    var previousReReadSameBookmark = linePreviousReRead.MakeBookmarkForReReadingLine();
                    Assert.Equal(expected: readSameBookmark.Position, actual: previousReReadSameBookmark.Position);
                    Assert.Equal(expected: readSameBookmark.CharIndex, actual: previousReReadSameBookmark.CharIndex);
                    Assert.Equal(expected: line.TextWithoutLineEnding, actual: linePreviousReRead.TextWithoutLineEnding);
                    Assert.Equal(expected: line.BookmarkingLineEnding, actual: linePreviousReRead.BookmarkingLineEnding);
                    Assert.Equal(expected: line.StartPosition, actual: linePreviousReRead.StartPosition);
                    Assert.Equal(expected: line.LastTextPosition, actual: linePreviousReRead.LastTextPosition);
                    Assert.Equal(expected: line.LastLineEndingPosition, actual: linePreviousReRead.LastLineEndingPosition);
                    Assert.Equal(expected: line.PositionAfterLineEnding,
                        actual: linePreviousReRead.PositionAfterLineEnding);
                }


                var ms2 = new MemoryStream(testsZ);
                var bookm2 = new BookmarkingStreamReader(ms2, Encoding.UTF8, false, bufferSize);
                if (debugThisTest) {
                    bookm2.LocallyTrackDebug();
                }

                previousResumedStreamReaders.Add(Tuple.Create(bookm2, expectNum));
                bookm2.ResumeFromBookmark(readSameBookmark);
                //var rePreReadDebugState = bookm2.DebugState();
                var lineReReadMaybe = bookm2.ReadDetailedLine();
                //var rePostReadDebugState = bookm2.DebugState();
                Assert.NotNull(lineReReadMaybe);

                var lineReRead = lineReReadMaybe.Value;
                if (line.PositionAfterLineEnding != lineReRead.PositionAfterLineEnding) {
                    var bp = 42;
                }
                var reReadSameBookmark = lineReRead.MakeBookmarkForReReadingLine();
                Assert.Equal(expected: readSameBookmark.Position, actual: reReadSameBookmark.Position);
                Assert.Equal(expected: readSameBookmark.CharIndex, actual: reReadSameBookmark.CharIndex);
                Assert.Equal(expected: line.TextWithoutLineEnding, actual: lineReRead.TextWithoutLineEnding);
                Assert.Equal(expected: line.BookmarkingLineEnding, actual: lineReRead.BookmarkingLineEnding);
                Assert.Equal(expected: line.StartPosition, actual: lineReRead.StartPosition);
                Assert.Equal(expected: line.LastTextPosition, actual: lineReRead.LastTextPosition);
                Assert.Equal(expected: line.LastLineEndingPosition, actual: lineReRead.LastLineEndingPosition);
                Assert.Equal(expected: line.PositionAfterLineEnding, actual: lineReRead.PositionAfterLineEnding);


                expectNum++;
            } while (true);
        }


#if NET4

        /// <summary>
        /// The reader assumes that any byte in a single byte encoding will always translate to one `char`, one UTF-16 code point.
        /// This test uses all supported encodings to check that this is true for every possible value.
        /// </summary>
        [Fact]
        public void Net4HandleSingleByteEncodingWithCharOutsideSingleUTF16CodePointRange() {
            var encodings = Encoding.GetEncodings().Select(ei => ei.GetEncoding()).Where(e => e.IsSingleByte).ToArray();

            foreach (var encoding in encodings) {
                for (var i = 0x00; i < 0x100; i++) {
                    var byteVal = (byte)i;
                    var byteArr = new[] { byteVal };
                    var chars = encoding.GetChars(byteArr);
                    if (chars.Length >= 2) { // avoid calculating the failure message each time
                        Assert.True(chars.Length < 2,
                            $"All supported single-byte encodings should be incapable of producing a value taking up more than a single UTF-16 code point; encoding {encoding.EncodingName}, byte value {byteVal:X2} seems to turn into multiple code points: {string.Join(" ", chars.Select(ch => "UTF16-" + ((int)ch).ToString("X4")))}. It's not an error for encodings to allow this, but Bookmarking Stream Reader is currently built on the assumption that it won't happen and will need adjusting if such an encoding is ever supported."
                            );
                    }
                }
            }
        }
#endif

#if NET_CORE

        /// <summary>
        /// The reader assumes that any byte in a single byte encoding will always translate to one `char`, one UTF-16 code point.
        /// This test uses all supported encodings to check that this is true for every possible value.
        /// </summary>
        [Fact]
        public void NetCoreHandleSingleByteEncodingWithCharOutsideSingleUTF16CodePointRange() {

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // GetEncodings does not return registered encoding provider encodings
            // see https://github.com/dotnet/corefx/issues/28944

            // Until then, here's all the supported encodings manually; see the
            // issue above for the source information.

            var supportedEncodings = new string[] {
                "437", // 437
                "arabic", // 28596
                "asmo-708", // 708
                "big5", // 950
                "big5-hkscs", // 950
                "ccsid00858", // 858
                "ccsid00924", // 20924
                "ccsid01140", // 1140
                "ccsid01141", // 1141
                "ccsid01142", // 1142
                "ccsid01143", // 1143
                "ccsid01144", // 1144
                "ccsid01145", // 1145
                "ccsid01146", // 1146
                "ccsid01147", // 1147
                "ccsid01148", // 1148
                "ccsid01149", // 1149
                "chinese", // 936
                "cn-big5", // 950
                "cn-gb", // 936
                "cp00858", // 858
                "cp00924", // 20924
                "cp01140", // 1140
                "cp01141", // 1141
                "cp01142", // 1142
                "cp01143", // 1143
                "cp01144", // 1144
                "cp01145", // 1145
                "cp01146", // 1146
                "cp01147", // 1147
                "cp01148", // 1148
                "cp01149", // 1149
                "cp037", // 37
                "cp1025", // 21025
                "cp1026", // 1026
                "cp1256", // 1256
                "cp273", // 20273
                "cp278", // 20278
                "cp280", // 20280
                "cp284", // 20284
                "cp285", // 20285
                "cp290", // 20290
                "cp297", // 20297
                "cp420", // 20420
                "cp423", // 20423
                "cp424", // 20424
                "cp437", // 437
                "cp500", // 500
                "cp50227", // 50227
                "cp850", // 850
                "cp852", // 852
                "cp855", // 855
                "cp857", // 857
                "cp858", // 858
                "cp860", // 860
                "cp861", // 861
                "cp862", // 862
                "cp863", // 863
                "cp864", // 864
                "cp865", // 865
                "cp866", // 866
                "cp869", // 869
                "cp870", // 870
                "cp871", // 20871
                "cp875", // 875
                "cp880", // 20880
                "cp905", // 20905
                "csbig5", // 950
                "cseuckr", // 51949
                "cseucpkdfmtjapanese", // 51932
                "csgb2312", // 936
                "csgb231280", // 936
                "csibm037", // 37
                "csibm1026", // 1026
                "csibm273", // 20273
                "csibm277", // 20277
                "csibm278", // 20278
                "csibm280", // 20280
                "csibm284", // 20284
                "csibm285", // 20285
                "csibm290", // 20290
                "csibm297", // 20297
                "csibm420", // 20420
                "csibm423", // 20423
                "csibm424", // 20424
                "csibm500", // 500
                "csibm870", // 870
                "csibm871", // 20871
                "csibm880", // 20880
                "csibm905", // 20905
                "csibmthai", // 20838
                "csiso2022jp", // 50221
                "csiso2022kr", // 50225
                "csiso58gb231280", // 936
                "csisolatin2", // 28592
                "csisolatin3", // 28593
                "csisolatin4", // 28594
                "csisolatin5", // 28599
                "csisolatin9", // 28605
                "csisolatinarabic", // 28596
                "csisolatincyrillic", // 28595
                "csisolatingreek", // 28597
                "csisolatinhebrew", // 28598
                "cskoi8r", // 20866
                "csksc56011987", // 949
                "cspc8codepage437", // 437
                "csshiftjis", // 932
                "cswindows31j", // 932
                "cyrillic", // 28595
                "din_66003", // 20106
                "dos-720", // 720
                "dos-862", // 862
                "dos-874", // 874
                "ebcdic-cp-ar1", // 20420
                "ebcdic-cp-be", // 500
                "ebcdic-cp-ca", // 37
                "ebcdic-cp-ch", // 500
                "ebcdic-cp-dk", // 20277
                "ebcdic-cp-es", // 20284
                "ebcdic-cp-fi", // 20278
                "ebcdic-cp-fr", // 20297
                "ebcdic-cp-gb", // 20285
                "ebcdic-cp-gr", // 20423
                "ebcdic-cp-he", // 20424
                "ebcdic-cp-is", // 20871
                "ebcdic-cp-it", // 20280
                "ebcdic-cp-nl", // 37
                "ebcdic-cp-no", // 20277
                "ebcdic-cp-roece", // 870
                "ebcdic-cp-se", // 20278
                "ebcdic-cp-tr", // 20905
                "ebcdic-cp-us", // 37
                "ebcdic-cp-wt", // 37
                "ebcdic-cp-yu", // 870
                "ebcdic-cyrillic", // 20880
                "ebcdic-de-273+euro", // 1141
                "ebcdic-dk-277+euro", // 1142
                "ebcdic-es-284+euro", // 1145
                "ebcdic-fi-278+euro", // 1143
                "ebcdic-fr-297+euro", // 1147
                "ebcdic-gb-285+euro", // 1146
                "ebcdic-international-500+euro", // 1148
                "ebcdic-is-871+euro", // 1149
                "ebcdic-it-280+euro", // 1144
                "ebcdic-jp-kana", // 20290
                "ebcdic-latin9--euro", // 20924
                "ebcdic-no-277+euro", // 1142
                "ebcdic-se-278+euro", // 1143
                "ebcdic-us-37+euro", // 1140
                "ecma-114", // 28596
                "ecma-118", // 28597
                "elot_928", // 28597
                "euc-cn", // 51936
                "euc-jp", // 51932
                "euc-kr", // 51949
                "extended_unix_code_packed_format_for_japanese", // 51932
                "gb18030", // 54936
                "gb2312", // 936
                "gb2312-80", // 936
                "gb231280", // 936
                "gb_2312-80", // 936
                "gbk", // 936
                "german", // 20106
                "greek", // 28597
                "greek8", // 28597
                "hebrew", // 28598
                "hz-gb-2312", // 52936
                "ibm-thai", // 20838
                "ibm00858", // 858
                "ibm00924", // 20924
                "ibm01047", // 1047
                "ibm01140", // 1140
                "ibm01141", // 1141
                "ibm01142", // 1142
                "ibm01143", // 1143
                "ibm01144", // 1144
                "ibm01145", // 1145
                "ibm01146", // 1146
                "ibm01147", // 1147
                "ibm01148", // 1148
                "ibm01149", // 1149
                "ibm037", // 37
                "ibm1026", // 1026
                "ibm273", // 20273
                "ibm277", // 20277
                "ibm278", // 20278
                "ibm280", // 20280
                "ibm284", // 20284
                "ibm285", // 20285
                "ibm290", // 20290
                "ibm297", // 20297
                "ibm420", // 20420
                "ibm423", // 20423
                "ibm424", // 20424
                "ibm437", // 437
                "ibm500", // 500
                "ibm737", // 737
                "ibm775", // 775
                "ibm850", // 850
                "ibm852", // 852
                "ibm855", // 855
                "ibm857", // 857
                "ibm860", // 860
                "ibm861", // 861
                "ibm862", // 862
                "ibm863", // 863
                "ibm864", // 864
                "ibm865", // 865
                "ibm866", // 866
                "ibm869", // 869
                "ibm870", // 870
                "ibm871", // 20871
                "ibm880", // 20880
                "ibm905", // 20905
                "irv", // 20105
                "iso-2022-jp", // 50220
                "iso-2022-jpeuc", // 51932
                "iso-2022-kr", // 50225
                "iso-2022-kr-7", // 50225
                "iso-2022-kr-7bit", // 50225
                "iso-2022-kr-8", // 51949
                "iso-2022-kr-8bit", // 51949
                "iso-8859-11", // 874
                "iso-8859-13", // 28603
                "iso-8859-15", // 28605
                "iso-8859-2", // 28592
                "iso-8859-3", // 28593
                "iso-8859-4", // 28594
                "iso-8859-5", // 28595
                "iso-8859-6", // 28596
                "iso-8859-7", // 28597
                "iso-8859-8", // 28598
                "iso-8859-8 visual", // 28598
                "iso-8859-8-i", // 38598
                "iso-8859-9", // 28599
                "iso-ir-101", // 28592
                "iso-ir-109", // 28593
                "iso-ir-110", // 28594
                "iso-ir-126", // 28597
                "iso-ir-127", // 28596
                "iso-ir-138", // 28598
                "iso-ir-144", // 28595
                "iso-ir-148", // 28599
                "iso-ir-149", // 949
                "iso-ir-58", // 936
                "iso8859-2", // 28592
                "iso_8859-15", // 28605
                "iso_8859-2", // 28592
                "iso_8859-2:1987", // 28592
                "iso_8859-3", // 28593
                "iso_8859-3:1988", // 28593
                "iso_8859-4", // 28594
                "iso_8859-4:1988", // 28594
                "iso_8859-5", // 28595
                "iso_8859-5:1988", // 28595
                "iso_8859-6", // 28596
                "iso_8859-6:1987", // 28596
                "iso_8859-7", // 28597
                "iso_8859-7:1987", // 28597
                "iso_8859-8", // 28598
                "iso_8859-8:1988", // 28598
                "iso_8859-9", // 28599
                "iso_8859-9:1989", // 28599
                "johab", // 1361
                "koi", // 20866
                "koi8", // 20866
                "koi8-r", // 20866
                "koi8-ru", // 21866
                "koi8-u", // 21866
                "koi8r", // 20866
                "korean", // 949
                "ks-c-5601", // 949
                "ks-c5601", // 949
                "ks_c_5601", // 949
                "ks_c_5601-1987", // 949
                "ks_c_5601-1989", // 949
                "ks_c_5601_1987", // 949
                "ksc5601", // 949
                "ksc_5601", // 949
                "l2", // 28592
                "l3", // 28593
                "l4", // 28594
                "l5", // 28599
                "l9", // 28605
                "latin2", // 28592
                "latin3", // 28593
                "latin4", // 28594
                "latin5", // 28599
                "latin9", // 28605
                "logical", // 28598
                "macintosh", // 10000
                "ms_kanji", // 932
                "norwegian", // 20108
                "ns_4551-1", // 20108
                "pc-multilingual-850+euro", // 858
                "sen_850200_b", // 20107
                "shift-jis", // 932
                "shift_jis", // 932
                "sjis", // 932
                "swedish", // 20107
                "tis-620", // 874
                "visual", // 28598
                "windows-1250", // 1250
                "windows-1251", // 1251
                "windows-1252", // 1252
                "windows-1253", // 1253
                "windows-1254", // 1254
                "windows-1255", // 1255
                "windows-1256", // 1256
                "windows-1257", // 1257
                "windows-1258", // 1258
                "windows-874", // 874
                "x-ansi", // 1252
                "x-chinese-cns", // 20000
                "x-chinese-eten", // 20002
                "x-cp1250", // 1250
                "x-cp1251", // 1251
                "x-cp20001", // 20001
                "x-cp20003", // 20003
                "x-cp20004", // 20004
                "x-cp20005", // 20005
                "x-cp20261", // 20261
                "x-cp20269", // 20269
                "x-cp20936", // 20936
                "x-cp20949", // 20949
                "x-cp50227", // 50227
                "x-ebcdic-koreanextended", // 20833
                "x-euc", // 51932
                "x-euc-cn", // 51936
                "x-euc-jp", // 51932
                "x-europa", // 29001
                "x-ia5", // 20105
                "x-ia5-german", // 20106
                "x-ia5-norwegian", // 20108
                "x-ia5-swedish", // 20107
                "x-iscii-as", // 57006
                "x-iscii-be", // 57003
                "x-iscii-de", // 57002
                "x-iscii-gu", // 57010
                "x-iscii-ka", // 57008
                "x-iscii-ma", // 57009
                "x-iscii-or", // 57007
                "x-iscii-pa", // 57011
                "x-iscii-ta", // 57004
                "x-iscii-te", // 57005
                "x-mac-arabic", // 10004
                "x-mac-ce", // 10029
                "x-mac-chinesesimp", // 10008
                "x-mac-chinesetrad", // 10002
                "x-mac-croatian", // 10082
                "x-mac-cyrillic", // 10007
                "x-mac-greek", // 10006
                "x-mac-hebrew", // 10005
                "x-mac-icelandic", // 10079
                "x-mac-japanese", // 10001
                "x-mac-korean", // 10003
                "x-mac-romanian", // 10010
                "x-mac-thai", // 10021
                "x-mac-turkish", // 10081
                "x-mac-ukrainian", // 10017
                "x-ms-cp932", // 932
                "x-sjis", // 932
                "x-x-big5", // 950
            };


            var encodings = supportedEncodings.Select(Encoding.GetEncoding).Where(e => e.IsSingleByte).ToArray();

            foreach (var encoding in encodings) {
                for (var i = 0x00; i < 0x100; i++) {
                    var byteVal = (byte)i;
                    var byteArr = new[] { byteVal };
                    var chars = encoding.GetChars(byteArr);
                    if (chars.Length >= 2) { // avoid calculating the failure message each time
                        Assert.True(chars.Length < 2,
                            $"All supported single-byte encodings should be incapable of producing a value taking up more than a single UTF-16 code point; encoding {encoding.EncodingName}, byte value {byteVal:X2} seems to turn into multiple code points: {string.Join(" ", chars.Select(ch => "UTF16-" + ((int)ch).ToString("X4")))}. It's not an error for encodings to allow this, but Bookmarking Stream Reader is currently built on the assumption that it won't happen and will need adjusting if such an encoding is ever supported."
                            );
                    }
                }
            }
        }
#endif

    }
}
