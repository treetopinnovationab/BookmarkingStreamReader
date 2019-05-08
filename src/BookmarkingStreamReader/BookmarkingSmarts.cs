using System;
using System.Text;

namespace Bookmarking {
    public struct ReadDetailedLineResult {
        private readonly LineBookmark _beforeReadingLineLineBookmark;

        public ReadDetailedLineResult(string textWithoutLineEnding, BookmarkingLineEnding bookmarkingLineEnding, long startPosition, long lastLineEndingPosition, long lastSeenCharIndex, LineBookmark beforeReadingLineLineBookmark) {
            _beforeReadingLineLineBookmark = beforeReadingLineLineBookmark;
            TextWithoutLineEnding = textWithoutLineEnding;
            BookmarkingLineEnding = bookmarkingLineEnding;
            StartPosition = startPosition;
            LastTextPosition = lastLineEndingPosition - (bookmarkingLineEnding == BookmarkingLineEnding.CarriageReturnLineFeed ? 2 : (bookmarkingLineEnding == BookmarkingLineEnding.None ? 0 : 1));
            LastLineEndingPosition = lastLineEndingPosition;
            LastSeenCharIndex = lastSeenCharIndex;
        }

        public LineBookmark MakeBookmarkForReReadingLine() {
            return _beforeReadingLineLineBookmark;
        }

        public LineBookmark MakeBookmarkForReadingNextLine() {
            return new LineBookmark(LastLineEndingPosition, LastSeenCharIndex);
        }

        public string TextWithoutLineEnding { get; }

        public string TextWithLineEnding => (BookmarkingLineEnding == BookmarkingLineEnding.None
            ? TextWithoutLineEnding
            : ((TextWithoutLineEnding) + (BookmarkingLineEnding == BookmarkingLineEnding.CarriageReturnLineFeed
                   ? "\r\n"
                   : (BookmarkingLineEnding == BookmarkingLineEnding.CarriageReturn ? "\r" : "\n"))));
        public BookmarkingLineEnding BookmarkingLineEnding { get; }
        public long StartPosition { get; }
        public long LastTextPosition { get; }
        public long LastLineEndingPosition { get; }
        public long LastSeenCharIndex { get; }
        public long PositionAfterLineEnding => LastLineEndingPosition + 1;
    }


    class BookmarkingCurrentPositionDetails {
        public void MovedToPosition(long bytePos, long charIndex) {
            ForgetState();
            _bytePositionStartOfCurrentBuffer = bytePos;
            _charPositionStartOfCurrentBuffer = charIndex;

            _byteAdvancementInfo = null;
            _singleByteCharsInCurrentBuffer = false;

            _currentBufferByteLength = 0;
            _currentBufferCharCount = 0;

            _debugAction?.Invoke($"moved to byte position {bytePos}; char index {charIndex}");
        }

        private ByteAdvancementInfo _byteAdvancementInfo = null;
        private bool _singleByteCharsInCurrentBuffer = false;

        private long _bytePositionStartOfCurrentBuffer = 0;
        private long _charPositionStartOfCurrentBuffer = 0;

        private int _currentBufferByteLength = 0;
        private int _currentBufferCharCount = 0;

        public void ReadBytesAndChars(int byteCount, int charCount, byte[] bytes, char[] chars, Encoding encoding) {

            if (_currentBufferByteLength > 0) {
                _bytePositionStartOfCurrentBuffer += _currentBufferByteLength;
                _debugAction?.Invoke($"had previous buffer; advanced byte position by {_currentBufferByteLength} to {_bytePositionStartOfCurrentBuffer}");
                _charPositionStartOfCurrentBuffer += _currentBufferCharCount;
                _debugAction?.Invoke($"had previous buffer; advanced char position by {_currentBufferCharCount} to {_charPositionStartOfCurrentBuffer}");
            }

            _debugAction?.Invoke($"new buffer contains {byteCount} bytes and {charCount} chars");

            _currentBufferByteLength = byteCount;
            _currentBufferCharCount = charCount;

            if (encoding.IsSingleByte) {
                _singleByteCharsInCurrentBuffer = true;
                _byteAdvancementInfo = null;
            } else if (encoding is UTF8Encoding u8) {
                if (_byteAdvancer == null || _byteAdvancer.LastUsedEncoding != u8) {
                    _byteAdvancer = new UTF8ByteAdvancer();
                }
                _singleByteCharsInCurrentBuffer = false;
                _byteAdvancementInfo = _byteAdvancer.BuildByteAdvancementInfo(bytes, byteCount, _bytePositionStartOfCurrentBuffer, chars, charCount, _charPositionStartOfCurrentBuffer, u8, _debugAction);
            } else if (encoding is UnicodeEncoding u16) {
                if (_byteAdvancer == null || _byteAdvancer.LastUsedEncoding != u16) {
                    _byteAdvancer = new UTF16ByteAdvancer();
                }
                _singleByteCharsInCurrentBuffer = false;
                _byteAdvancementInfo = _byteAdvancer.BuildByteAdvancementInfo(bytes, byteCount, _bytePositionStartOfCurrentBuffer, chars, charCount, _charPositionStartOfCurrentBuffer, u16, _debugAction);
            }
        }

        private IByteAdvancer _byteAdvancer = null;

        public long GetBytePositionOfStartOfCurrentBuffer() {
            return _bytePositionStartOfCurrentBuffer;
        }

        public long GetCharPositionOfStartOfCurrentBuffer() {
            return _charPositionStartOfCurrentBuffer;
        }

        public long AbsoluteBytePositionOfCharIndexInCurrentBuffer(int charPos) {
            if (_singleByteCharsInCurrentBuffer) {
                var res = _bytePositionStartOfCurrentBuffer + charPos;
                _debugAction?.Invoke($"absolute byte position of char index {charPos} = {res}");
                return res;
            } else if (_byteAdvancementInfo != null) {
                var charIndexesAtByteIndexInCurrentBuffer = _byteAdvancementInfo.CharIndexesAtByteIndex;
                for (var byteIndex = 0; byteIndex < charIndexesAtByteIndexInCurrentBuffer.Length; byteIndex++) {
                    var charIndex = charIndexesAtByteIndexInCurrentBuffer[byteIndex];
                    if (charIndex >= charPos) {
                        var negativeAdjustment =
                            (byteIndex == 0 && charPos == charIndex) ? _byteAdvancementInfo.FirstCharExtendsBackByteCount : 0;
                        var res = _bytePositionStartOfCurrentBuffer + byteIndex - negativeAdjustment;
                        _debugAction?.Invoke($"absolute byte position of char index {charPos} = {res}");
                        return res;
                    }

                    if ((charIndex + 1) == charPos && _byteAdvancementInfo.ExtraIncompleteCharWithByteCount == 0) {
                        var res = _bytePositionStartOfCurrentBuffer + byteIndex + 1;
                        _debugAction?.Invoke($"character just after valid data; absolute byte position of char index {charPos} = {res}");
                        return res;
                    }
                }
            }

            return -1;
        }

        public long AbsoluteCharPositionOfCharIndexInCurrentBuffer(int charPos) {
            if (_singleByteCharsInCurrentBuffer) {
                return _charPositionStartOfCurrentBuffer + charPos;
            } else if (_byteAdvancementInfo != null) {
                var charIndexesAtByteIndexInCurrentBuffer = _byteAdvancementInfo.CharIndexesAtByteIndex;
                for (var byteIndex = 0; byteIndex < charIndexesAtByteIndexInCurrentBuffer.Length; byteIndex++) {
                    var charIndex = charIndexesAtByteIndexInCurrentBuffer[byteIndex];
                    if (charIndex >= charPos) {
                        return _charPositionStartOfCurrentBuffer + charIndex;
                    }

                    if ((charIndex + 1) == charPos && _byteAdvancementInfo.ExtraIncompleteCharWithByteCount == 0) {
                        return _charPositionStartOfCurrentBuffer + charIndex + 1;
                    }
                }
            }

            return -1;
        }


        interface IByteAdvancer {
            void ResetState();

            Encoding LastUsedEncoding { get; }

            ByteAdvancementInfo BuildByteAdvancementInfo(byte[] bytes, int byteCount, long byteOffset, char[] chars, int charCount, long charOffset, Encoding en,
                Action<string> logger);
        }

        class ByteAdvancementInfo {
            public ByteAdvancementInfo(int[] charIndexesAtByteIndex, int firstCharExtendsBackByteCount, int extraIncompleteCharWithByteCount) {
                CharIndexesAtByteIndex = charIndexesAtByteIndex;
                FirstCharExtendsBackByteCount = firstCharExtendsBackByteCount;
                ExtraIncompleteCharWithByteCount = extraIncompleteCharWithByteCount;
            }

            public int[] CharIndexesAtByteIndex { get; }
            public int FirstCharExtendsBackByteCount { get; }
            public int ExtraIncompleteCharWithByteCount { get; }
        }

        class UTF8ByteAdvancer : IByteAdvancer {
            public void ResetState() {
                _currentCharByteRun = notSet;
                _expectedCharByteRun = notSet;

                _utf8DataByte1 = 0;
                _utf8DataByte2 = 0;
                _utf8DataByte3 = 0;
                _utf8DataByte4 = 0;
            }

            public Encoding LastUsedEncoding { get; private set; }

            const byte notSet = 0xff;

            byte _currentCharByteRun = notSet;
            byte _expectedCharByteRun = notSet;

            byte _utf8DataByte1 = 0;
            byte _utf8DataByte2 = 0;
            byte _utf8DataByte3 = 0;
            byte _utf8DataByte4 = 0;


            //     byte index:  b00 b01 b02 b03 b04 b05 b06 b07 b08
            //     bytes:         A  B1  B2  C1  C2  C3  D   E1  E2 
            //     char index:  c00 c01--+  c02------+  c03 c04--+
            //
            //     UTF-8
            //     normal length: 1
            //     only store exceptions:
            //     - [at byte idx 01: 2 bytes]
            //     - [at byte idx 03: 3 bytes]
            //     - [at byte idx 07: 2 bytes]

            /*

            bytes   bits    UTF-8 representation
            -----   ----    -----------------------------------
            1        7      0vvvvvvv
            2       11      110vvvvv 10vvvvvv
            3       16      1110vvvv 10vvvvvv 10vvvvvv
            4       21      11110vvv 10vvvvvv 10vvvvvv 10vvvvvv
            -----   ----    -----------------------------------

            Surrogate:
            Real Unicode value = (HighSurrogate - 0xD800) * 0x400 + (LowSurrogate - 0xDC00) + 0x10000

            */

            public ByteAdvancementInfo BuildByteAdvancementInfo(byte[] bytes, int byteCount, long byteOffset, char[] chars, int charCount, long charOffset, Encoding en,
                Action<string> logger) {

                LastUsedEncoding = en;

                var utf16CharIndex = 0;
                var unicodeScalarIndex = 0;

                const byte isASCIIMask                               = 0b1_0000000;
                const byte isASCIIAfterMaskShouldBe                  = 0b0_0000000;
                const byte isContinuationByteMask                    = 0b11_000000;
                const byte isContinuationByteAfterMaskShouldBe       = 0b10_000000;
                const byte isDoubleByteFirstByteMask                 = 0b111_00000;
                const byte isDoubleByteFirstByteAfterMaskShouldBe    = 0b110_00000;
                const byte isTripleByteFirstByteMask                 = 0b1111_0000;
                const byte isTripleByteFirstByteAfterMaskShouldBe    = 0b1110_0000;
                const byte isQuadrupleByteFirstByteMask              = 0b11111_000;
                const byte isQuadrupleByteFirstByteAfterMaskShouldBe = 0b11110_000;

                const byte continuationByteDataMask = 0b00_111111;
                const byte doubleByteDataMask       = 0b000_11111;
                const byte tripleByteDataMask       = 0b0000_1111;
                const byte quadrupleByteDataMask    = 0b00000_111;


                const int possibleExtraBytes = 4;

                var charByteAdvancements = new byte[byteCount + possibleExtraBytes];
                var charIndexesAtByteIndex = new int[byteCount + possibleExtraBytes];

                var firstCharExtendsBackByteCount = 0;

                if (_currentCharByteRun != notSet && _expectedCharByteRun != notSet &&
                    _expectedCharByteRun > _currentCharByteRun) {
                    firstCharExtendsBackByteCount = _currentCharByteRun;
                }

                for (int i = 0; i < byteCount; i++) {
                    var b = bytes[i];

                    charIndexesAtByteIndex[i] = utf16CharIndex;

                    if ((b & isASCIIMask) == isASCIIAfterMaskShouldBe) {
                        charByteAdvancements[utf16CharIndex] = 1;

                        _currentCharByteRun = notSet;
                        _expectedCharByteRun = notSet;

                        logger?.Invoke($"byte at index {i} is ASCII: {b:X2}; single character, single byte");

                        unicodeScalarIndex++;
                        utf16CharIndex++;
                        continue;
                    }

                    if ((b & isContinuationByteMask) == isContinuationByteAfterMaskShouldBe) {
                        var utf8DataByte = (byte)(b & continuationByteDataMask);
                        if (_currentCharByteRun == 1) {
                            logger?.Invoke($"byte at index {i} is second continuation byte: unmasked: {b:X2}");
                            _utf8DataByte2 = utf8DataByte;
                        } else if (_currentCharByteRun == 2) {
                            logger?.Invoke($"byte at index {i} is third continuation byte: unmasked: {b:X2}");
                            _utf8DataByte3 = utf8DataByte;
                        } else if (_currentCharByteRun == 3) {
                            logger?.Invoke($"byte at index {i} is fourth continuation byte: unmasked: {b:X2}");
                            _utf8DataByte4 = utf8DataByte;
                        }

                        _currentCharByteRun++;
                        if (_expectedCharByteRun == _currentCharByteRun) {
                            long unicodeScalar = 0;

                            if (_expectedCharByteRun == 2) {
                                unicodeScalar = ((long)(_utf8DataByte1 & 0x1f) << 6) | ((long)(_utf8DataByte2 & 0x3f) << 0);
                            } else if (_expectedCharByteRun == 3) {
                                unicodeScalar = ((long)(_utf8DataByte1 & 0x0f) << 12) | ((long)(_utf8DataByte2 & 0x3f) << 6) | ((long)(_utf8DataByte3 & 0x3f) << 0);
                            } else if (_expectedCharByteRun == 4) {
                                unicodeScalar = ((long)(_utf8DataByte1 & 0x07) << 18) |
                                                ((long)(_utf8DataByte2 & 0x3f) << 12) |
                                                ((long)(_utf8DataByte3 & 0x3f) << 6) |
                                                ((long)(_utf8DataByte4 & 0x3f) << 0);
                            }

                            charByteAdvancements[utf16CharIndex] = _currentCharByteRun;
                            _currentCharByteRun = notSet;
                            _expectedCharByteRun = notSet;

                            logger?.Invoke($"byte at index {i} is final continuation byte: unicode scalar: U+{unicodeScalar:X5}");

                            var requiredUTF16Bytes = (unicodeScalar > 0xffff) ? 4 : 2;
                            var requiredUTF16Chars = (unicodeScalar > 0xffff) ? 2 : 1;

                            logger?.Invoke($"- num required UTF-16 bytes: {requiredUTF16Bytes}, chars: {requiredUTF16Chars}");


                            utf16CharIndex += requiredUTF16Chars;
                            unicodeScalarIndex++;
                        }

                        continue;
                    }

                    if ((b & isDoubleByteFirstByteMask) == isDoubleByteFirstByteAfterMaskShouldBe) {
                        _expectedCharByteRun = 2;
                        _currentCharByteRun = 1;

                        logger?.Invoke($"byte at index {i} is start of double byte run: {b:X2}");

                        _utf8DataByte1 = (byte)(b & doubleByteDataMask);

                        continue;
                    }
                    if ((b & isTripleByteFirstByteMask) == isTripleByteFirstByteAfterMaskShouldBe) {
                        _expectedCharByteRun = 3;
                        _currentCharByteRun = 1;

                        logger?.Invoke($"byte at index {i} is start of triple byte run: {b:X2}");

                        _utf8DataByte1 = (byte)(b & tripleByteDataMask);
                        continue;
                    }
                    if ((b & isQuadrupleByteFirstByteMask) == isQuadrupleByteFirstByteAfterMaskShouldBe) {
                        _expectedCharByteRun = 4;
                        _currentCharByteRun = 1;

                        logger?.Invoke($"byte at index {i} is start of quadruple byte run: {b:X2}");

                        _utf8DataByte1 = (byte)(b & quadrupleByteDataMask);
                        continue;
                    }
                }

                var extraOffset = byteCount;
                var extraIncompleteCharWithByteCount = 0;

                if (_currentCharByteRun != notSet && _expectedCharByteRun != notSet &&
                    _expectedCharByteRun > _currentCharByteRun) {
                    extraIncompleteCharWithByteCount = _currentCharByteRun;
                }

                Array.Resize(ref charIndexesAtByteIndex, extraOffset);

                Array.Resize(ref charByteAdvancements, utf16CharIndex);

                var byteIndex = 0;
                foreach (var charIndexAtByteIndex in charIndexesAtByteIndex) {
                    logger?.Invoke($"byte at index {byteIndex} corresponds to char index {charIndexAtByteIndex} (abs byte: {(byteIndex + byteOffset)}, char {(charIndexAtByteIndex + charOffset)})");
                    byteIndex++;
                }

                return new ByteAdvancementInfo(charIndexesAtByteIndex, firstCharExtendsBackByteCount, extraIncompleteCharWithByteCount);
            }
        }

        class UTF16ByteAdvancer : IByteAdvancer {
            public void ResetState() {
                _firstByte = null;
                _previousSurrogateChar = null;
            }

            public Encoding LastUsedEncoding { get; private set; }


            /*
                To encode U+10437 (𐐷) to UTF-16:

            Subtract 0x10000 from the code point, leaving 0x0437.
            For the high surrogate, shift right by 10 (divide by 0x400), then add 0xD800, resulting in 0x0001 + 0xD800 = 0xD801.
            For the low surrogate, take the low 10 bits (remainder of dividing by 0x400), then add 0xDC00, resulting in 0x0037 + 0xDC00 = 0xDC37.
            
                To decode U+10437 (𐐷) from UTF-16:

            Take the high surrogate (0xD801) and subtract 0xD800, then multiply by 0x400, resulting in 0x0001 × 0x400 = 0x0400.
            Take the low surrogate (0xDC37) and subtract 0xDC00, resulting in 0x37.
            Add these two results together (0x0437), and finally add 0x10000 to get the final decoded UTF-32 code point, 0x10437.

             */

            private const int _utf16BigEndianCodePage = 1201;
            private const int _utf16LittleEndianCodePage = 1200;

            private byte? _firstByte = null;
            private char? _previousSurrogateChar = null;

            public ByteAdvancementInfo BuildByteAdvancementInfo(byte[] bytes, int byteCount, long byteOffset, char[] chars, int charCount, long charOffset, Encoding en,
                Action<string> logger) {

                LastUsedEncoding = en;

                var isBigEndian = (en.CodePage == _utf16BigEndianCodePage);

                var utf16CharIndex = 0;
                var unicodeScalarIndex = 0;

                const int lowSurrogateStarts  = 0xD800;
                const int lowSurrogateEnds    = 0xDBFF;
                const int highSurrogateStarts = 0xDC00;
                const int highSurrogateEnds   = 0xDFFF;

                var charIndexesAtByteIndex = new int[byteCount];

                var firstCharExtendsBackByteCount = (_firstByte == null) ? 0 : 1;

                //if (_currentCharByteRun != notSet && _expectedCharByteRun != notSet &&
                //    _expectedCharByteRun > _currentCharByteRun) {
                //    firstCharExtendsBackByteCount = _currentCharByteRun;
                //}

                for (int i = 0; i < byteCount; i++) {
                    var b = bytes[i];

                    charIndexesAtByteIndex[i] = utf16CharIndex;

                    if (_firstByte == null) {
                        _firstByte = b;
                    } else {
                        var lower = (isBigEndian) ? _firstByte.Value : b;
                        var upper = (isBigEndian) ? b : _firstByte.Value;
                        var utf16Char = (char)((upper << 8) + lower);

                        _firstByte = null;

                        if (_previousSurrogateChar != null) {
                            var previousSurrogateChar = _previousSurrogateChar;

                            // ..
                            unicodeScalarIndex++;

                            _previousSurrogateChar = null;
                        } else {
                            _previousSurrogateChar = utf16Char;
                        }

                        utf16CharIndex++;
                    }
                }

                var extraOffset = byteCount;

                var extraIncompleteCharWithByteCount = (_firstByte == null) ? 0 : 1;

                Array.Resize(ref charIndexesAtByteIndex, extraOffset);

                var byteIndex = 0;
                foreach (var charIndexAtByteIndex in charIndexesAtByteIndex) {
                    logger?.Invoke($"byte at index {byteIndex} corresponds to char index {charIndexAtByteIndex} (abs byte: {(byteIndex + byteOffset)}, char {(charIndexAtByteIndex + charOffset)})");
                    byteIndex++;
                }

                return new ByteAdvancementInfo(charIndexesAtByteIndex, firstCharExtendsBackByteCount, extraIncompleteCharWithByteCount);
            }
        }




        public void ForgetState() {
            _bytePositionStartOfCurrentBuffer = 0;
            _charPositionStartOfCurrentBuffer = 0;

            _byteAdvancementInfo = null;
            _singleByteCharsInCurrentBuffer = false;

            _currentBufferByteLength = 0;
            _currentBufferCharCount = 0;

            _byteAdvancer = null;
        }

        private Action<string> _debugAction;

        public void SetDebug(Action<string> debugAction) {
            _debugAction = debugAction;
        }

        public void MovedPastPreambleOfByteLength(int preambleLength) {
            _bytePositionStartOfCurrentBuffer += preambleLength;
        }

        public string GetDebugState() {
            var sb = new StringBuilder();
            sb.AppendLine($"_bytePositionStartOfCurrentBuffer={_bytePositionStartOfCurrentBuffer}");
            sb.AppendLine($"_charIndexesAtByteIndexInCurrentBuffer={(_byteAdvancementInfo == null ? "null" : string.Join(",", _byteAdvancementInfo.CharIndexesAtByteIndex))}");
            sb.AppendLine($"_firstCharExtendsBackByteCount={(_byteAdvancementInfo == null ? 0 : _byteAdvancementInfo.FirstCharExtendsBackByteCount)}");
            sb.AppendLine($"_extraIncompleteCharWithByteCount={(_byteAdvancementInfo == null ? 0 : _byteAdvancementInfo.ExtraIncompleteCharWithByteCount)}");
            sb.AppendLine($"_charPositionStartOfCurrentBuffer={_charPositionStartOfCurrentBuffer}");
            sb.AppendLine($"_currentBufferByteLength={_currentBufferByteLength}");
            sb.AppendLine($"_currentBufferCharCount={_currentBufferCharCount}");
            sb.AppendLine($"_singleByteCharsInCurrentBuffer={_singleByteCharsInCurrentBuffer}");
            return sb.ToString();
        }
    }

    internal static class BookmarkingStreamReaderCommon {
        public static bool SupportsReading(Encoding knownEncoding) {
            if (knownEncoding.IsSingleByte) {
                return true;
            }
            if (knownEncoding.WebName == Encoding.UTF8.WebName) {
                return true;
            }
            if (knownEncoding.WebName.StartsWith("utf-16")) {
                return true;
            }
            return false;
        }
    }

    public enum BookmarkingLineEnding : byte {
        None,
        CarriageReturn, // \r
        CarriageReturnLineFeed, // \r\n
        LineFeed // \n
    }

    public struct LineBookmark {
        /// <summary>
        /// The byte position last read.
        /// </summary>
        public long Position { get; }
        /// <summary>
        /// The position of the `char` last read.
        /// </summary>
        public long CharIndex { get; }

        public LineBookmark(long position, long charIndex) {
            Position = position;
            CharIndex = charIndex;
        }
    }
}