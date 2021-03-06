/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

#pragma warning disable 1634, 1691

using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Management.Automation.Internal;
using System.Runtime.Serialization;
using Dbg = System.Management.Automation.Diagnostics;

#if CORECLR
// Use stub for SerializableAttribute, NonSerializedAttribute and ISerializable related types.
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

namespace System.Management.Automation
{
    /// <summary>
    /// Provides enumerated values to use to set wildcard pattern 
    /// matching options.
    /// </summary>
    [Flags]
    public enum WildcardOptions
    {
        /// <summary>
        /// Indicates that no special processing is required.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies that the wildcard pattern is compiled to an assembly. 
        /// This yields faster execution but increases startup time.
        /// </summary>
        Compiled = 1,
        
        /// <summary>
        /// Specifies case-insensitive matching.
        /// </summary>
        IgnoreCase = 2,

        /// <summary>
        /// Specifies culture-invariant matching.
        /// </summary>
        CultureInvariant = 4
    };

    /// <summary>
    /// Represents a wildcard pattern.
    /// </summary>
    public sealed class WildcardPattern
    {
        //
        // char that escapes special chars
        //
        const char escapeChar = '`';

        //
        // we convert a wildcard pattern to a predicate
        //
        private Predicate<string> _isMatch;

        //
        // wildcard pattern
        //
        readonly string pattern;
        internal string Pattern
        {
            get { return pattern; }
        }

        //
        // options that control match behavior
        // 
        readonly WildcardOptions options = WildcardOptions.None;
        internal WildcardOptions Options
        {
            get { return options; }
        }

        /// <summary>
        /// wildcard pattern converted to regex pattern.
        /// </summary>
        internal string PatternConvertedToRegex
        {
            get
            {
                var patternRegex = WildcardPatternToRegexParser.Parse(this);
                return patternRegex.ToString();
            }
        }

        /// <summary>
        /// Initializes and instance of the WildcardPattern class 
        /// for the specified wildcard pattern.
        /// </summary>
        /// <param name="pattern">The wildcard pattern to match</param>
        /// <returns>The constructed WildcardPattern object</returns>
        /// <remarks> if wildCardType == None, the pattern does not have wild cards</remarks>
        public WildcardPattern(string pattern)
        {
            if (pattern == null)
            {
                throw PSTraceSource.NewArgumentNullException("pattern");
            }

            this.pattern = pattern;
        }

        /// <summary>
        /// Initializes an instance of the WildcardPattern class for 
        /// the specified wildcard pattern expression, with options 
        /// that modify the pattern.
        /// </summary>
        /// <param name="pattern">The wildcard pattern to match.</param>
        /// <param name="options">Wildcard options</param>
        /// <returns>The constructed WildcardPattern object</returns>
        /// <remarks> if wildCardType == None, the pattern does not have wild cards  </remarks>
        public WildcardPattern(string pattern,
                               WildcardOptions options)
        {
            if (pattern == null)
            {
                throw PSTraceSource.NewArgumentNullException("pattern");
            }

            this.pattern = pattern;
            this.options = options;
        }

        static readonly WildcardPattern matchAllIgnoreCasePattern = new WildcardPattern("*", WildcardOptions.None);

        /// <summary>
        /// Create a new WildcardPattern, or return an already created one.
        /// </summary>
        /// <param name="pattern">The pattern</param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static WildcardPattern Get(string pattern, WildcardOptions options)
        {
            if (pattern == null)
                throw PSTraceSource.NewArgumentNullException("pattern");

            if (pattern.Length == 1 && pattern[0] == '*')
                return matchAllIgnoreCasePattern;

            return new WildcardPattern(pattern, options);
        }

        /// <summary>
        /// Instantiate internal regex member if not already done.
        /// </summary>
        ///
        /// <returns> true on success, false otherwise </returns>
        ///
        /// <remarks>  </remarks>
        ///
        private void Init()
        {
            if (_isMatch == null)
            {
                if (pattern.Length == 1 && pattern[0] == '*')
                {
                    _isMatch = _ => true;
                }
                else
                {
                    var matcher = new WildcardPatternMatcher(this);
                    _isMatch = matcher.IsMatch;
                }
            }
        }

        /// <summary>
        /// Indicates whether the wildcard pattern specified in the WildcardPattern
        /// constructor finds a match in the input string.
        /// </summary>
        /// <param name="input">The string to search for a match.</param>
        /// <returns>true if the wildcard pattern finds a match; otherwise, false</returns>
        public bool IsMatch(string input)
        {
            Init();
            return input != null && _isMatch(input);
        }

        /// <summary>
        /// Escape special chars, except for those specified in <paramref name="charsNotToEscape"/>, in a string by replacing them with their escape codes.
        /// </summary>
        /// <param name="pattern">The input string containing the text to convert.</param>
        /// <param name="charsNotToEscape">Array of characters that not to escape</param>
        /// <returns>
        /// A string of characters with any metacharacters, except for those specified in <paramref name="charsNotToEscape"/>, converted to their escaped form.
        /// </returns>
        internal static string Escape(string pattern, char[] charsNotToEscape)
        {
            #pragma warning disable 56506

            if (pattern == null)
            {
                throw PSTraceSource.NewArgumentNullException("pattern");
            }

            if (charsNotToEscape == null)
            {
                throw PSTraceSource.NewArgumentNullException("charsNotToEscape");
            }

            char[] temp = new char[pattern.Length * 2 + 1];
            int tempIndex = 0;

            for (int i=0; i<pattern.Length; i++)
            {
                char ch = pattern[i];

                //
                // if it is a wildcard char, escape it
                //
                if (IsWildcardChar(ch) && !charsNotToEscape.Contains(ch))
                {
                    temp[tempIndex++] = escapeChar;
                }

                temp[tempIndex++] = ch;
            }

            string s = null;

            if (tempIndex > 0)
            {
                s = new string(temp, 0, tempIndex);
            }
            else
            {
                s = String.Empty;
            }

            return s;

            #pragma warning restore 56506
        }

        /// <summary>
        /// Escape special chars in a string by replacing them with their escape codes.
        /// </summary>
        /// <param name="pattern">The input string containing the text to convert.</param>
        /// <returns>
        /// A string of characters with any metacharacters converted to their escaped form.
        /// </returns>
        public static string Escape(string pattern)
        {
            return Escape(pattern, Utils.EmptyArray<char>());
        }

        /// <summary>
        /// Checks to see if the given string has any wild card characters in it.
        /// </summary>
        /// <param name="pattern">
        /// String which needs to be checked for the presence of wildcard chars
        /// </param>
        /// <returns> true if the string has wild card chars, false otherwise. </returns>
        /// <remarks>
        /// Currently { '*', '?', '[' } are considered wild card chars and
        /// '`' is the escape character.
        /// </remarks>
        public static bool ContainsWildcardCharacters(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            bool result = false;

            for (int index = 0; index < pattern.Length; ++index)
            {
                if (IsWildcardChar(pattern[index]))
                {
                    result = true;
                    break;
                }

                // If it is an escape character then advance past
                // the next character

                if (pattern[index] == escapeChar)
                {
                    ++index;
                }
            }
            return result;
        }

        /// <summary>
        /// Unescapes any escaped characters in the input string.
        /// </summary>
        /// <param name="pattern">
        /// The input string containing the text to convert. 
        /// </param>
        /// <returns>
        /// A string of characters with any escaped characters 
        /// converted to their unescaped form.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="pattern" /> is null.
        /// </exception>
        public static string Unescape(string pattern)
        {
            if (pattern == null)
            {
                throw PSTraceSource.NewArgumentNullException("pattern");
            }

            char[] temp = new char[pattern.Length];
            int tempIndex = 0;
            bool prevCharWasEscapeChar = false;

            for (int i=0; i<pattern.Length; i++)
            {
                char ch = pattern[i];

                if ( ch == escapeChar )
                {
                    if (prevCharWasEscapeChar)
                    {
                        temp[tempIndex++] = ch;
                        prevCharWasEscapeChar = false;
                    }
                    else
                    {
                        prevCharWasEscapeChar = true;
                    }
                    continue;
                }

                if (prevCharWasEscapeChar)
                {
                    if (!IsWildcardChar(ch))
                    {
                        temp[tempIndex++] = escapeChar;
                    }
                }

                temp[tempIndex++] = ch;
                prevCharWasEscapeChar = false;
            }

            // Need to account for a trailing escape character as a real
            // character

            if (prevCharWasEscapeChar)
            {
                temp[tempIndex++] = escapeChar;
                prevCharWasEscapeChar = false;
            }

            string s = null;

            if (tempIndex > 0)
            {
                s = new string(temp, 0, tempIndex);
            }
            else
            {
                s = String.Empty;
            }

            return s;
        } // Unescape

        private static bool IsWildcardChar(char ch)
        {
            return (ch == '*') || (ch == '?') || (ch == '[') || (ch == ']');
        }

        /// <summary>
        /// Converts this wildcard to a string that can be used as a right-hand-side operand of the LIKE operator of WQL.
        /// For example: "a*" will be converted to "a%". 
        /// </summary>
        /// <returns></returns>
        public string ToWql()
        {
            bool needsClientSideFiltering;
            string likeOperand = Microsoft.PowerShell.Cmdletization.Cim.WildcardPatternToCimQueryParser.Parse(this, out needsClientSideFiltering);
            if (!needsClientSideFiltering)
            {
                return likeOperand;
            }
            else
            {
                throw new PSInvalidCastException(
                    "UnsupportedWildcardToWqlConversion",
                    null,
                    ExtendedTypeSystem.InvalidCastException,
                    this.Pattern,
                    this.GetType().FullName,
                    "WQL");
            }
        }
    }

    /// <summary>
    /// Thrown when a wildcard pattern is invalid.
    /// </summary>
    [Serializable]
    public class WildcardPatternException : RuntimeException
    {
        /// <summary>
        /// Constructor for class WildcardPatternException that takes 
        /// an ErrorRecord to use in constructing this exception.
        /// </summary>
        /// <remarks>This is the recommended constructor to use for this exception.</remarks>
        /// <param name="errorRecord">
        /// ErrorRecord object containing additional information about the error condition.
        /// </param>
        /// <returns> constructed object </returns>
        internal WildcardPatternException (ErrorRecord errorRecord)
            : base( RetrieveMessage(errorRecord) )
        {
            if (null == errorRecord)
            {
                throw new ArgumentNullException ("errorRecord");
            }
            _errorRecord = errorRecord;
        }
        [NonSerialized]
        private ErrorRecord _errorRecord;

        /// <summary>
        /// Constructs an instance of the WildcardPatternException object.
        /// </summary>
        public WildcardPatternException()
        {
        }

        /// <summary>
        /// Constructs an instance of the WildcardPatternException object taking
        /// a message parameter to use in cnstructing the exception.
        /// </summary>
        /// <param name="message">The string to use as the exception message</param>
        public WildcardPatternException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor for class WildcardPatternException that takes both a message to use
        /// and an inner exception to include in this object.
        /// </summary>
        /// <param name="message">The exception message to use</param>
        /// <param name="innerException">The innerException object to encapsulate.</param>
        public WildcardPatternException(string message,
                                        Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor for class WildcardPatternException for serialization.
        /// </summary>
        /// <param name="info">serialization information</param>
        /// <param name="context">streaming context</param>
        protected WildcardPatternException(SerializationInfo info,
                                        StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// A base class for parsers of <see cref="WildcardPattern"/> patterns.
    /// </summary>
    internal abstract class WildcardPatternParser
    {
        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate 
        /// the beginning of the wildcard pattern.
        /// Default implementation simply returns.
        /// </summary>
        /// <param name="pattern">
        /// <see cref="WildcardPattern"/> object that includes both 
        /// the text of the pattern (<see cref="WildcardPattern.Pattern"/>) 
        /// and the pattern options (<see cref="WildcardPattern.Options"/>)
        /// </param>
        protected virtual void BeginWildcardPattern(WildcardPattern pattern)
        {
        }

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate that the next
        /// part of the pattern should match 
        /// a literal character <paramref name="c"/>.
        /// </summary>
        protected abstract void AppendLiteralCharacter(char c);

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate that the next
        /// part of the pattern should match 
        /// any string, including an empty string.
        /// </summary>
        protected abstract void AppendAsterix();

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate that the next
        /// part of the pattern should match 
        /// any single character. 
        /// </summary>
        protected abstract void AppendQuestionMark();

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate the end of the wildcard pattern.
        /// Default implementation simply returns.
        /// </summary>
        protected virtual void EndWildcardPattern()
        {
        }

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate 
        /// the beginning of a bracket expression.
        /// </summary>
        /// <remarks>
        /// Bracket expressions of <see cref="WildcardPattern"/> are 
        /// a greatly simplified version of bracket expressions of POSIX wildcards 
        /// (http://www.opengroup.org/onlinepubs/9699919799/functions/fnmatch.html).
        /// Only literal characters and character ranges are supported.  
        /// Negation (with either '!' or '^' characters), 
        /// character classes ([:alpha:]) 
        /// and other advanced features are not supported.
        /// </remarks>
        protected abstract void BeginBracketExpression();

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate that the bracket expression
        /// should include a literal character <paramref name="c"/>.
        /// </summary>
        protected abstract void AppendLiteralCharacterToBracketExpression(char c);

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate that the bracket expression
        /// should include all characters from character range 
        /// starting at <paramref name="startOfCharacterRange"/> 
        /// and ending at <paramref name="endOfCharacterRange"/>
        /// </summary>
        protected abstract void AppendCharacterRangeToBracketExpression(
                        char startOfCharacterRange, 
                        char endOfCharacterRange);

        /// <summary>
        /// Called from <see cref="Parse"/> method to indicate the end of a bracket expression.
        /// </summary>
        protected abstract void EndBracketExpression();

        /// <summary>
        /// PowerShell v1 and v2 treats all characters inside
        /// <paramref name="brackedExpressionContents"/> as literal characters, 
        /// except '-' sign which denotes a range.  In particular it means that
        /// '^', '[', ']' are escaped within the bracket expression and don't
        /// have their regex-y meaning.
        /// </summary>
        /// <param name="brackedExpressionContents"></param>
        /// <param name="bracketExpressionOperators"></param>
        /// <param name="pattern"></param>
        /// <remarks>
        /// This method should be kept "internal"
        /// </remarks>
        internal void AppendBracketExpression(string brackedExpressionContents, string bracketExpressionOperators, string pattern)
        {
            this.BeginBracketExpression();

            int i = 0;
            while (i < brackedExpressionContents.Length)
            {
                if (((i + 2) < brackedExpressionContents.Length) && 
                                (bracketExpressionOperators[i + 1] == '-'))
                {
                    char lowerBound = brackedExpressionContents[i];
                    char upperBound = brackedExpressionContents[i + 2];
                    i += 3;

                    if (lowerBound > upperBound)
                    {
                        throw NewWildcardPatternException(pattern);
                    }

                    this.AppendCharacterRangeToBracketExpression(lowerBound, upperBound);
                }
                else
                {
                    this.AppendLiteralCharacterToBracketExpression(brackedExpressionContents[i]);
                    i++;
                }
            }

            this.EndBracketExpression();
        }

        /// <summary>
        /// Parses <paramref name="pattern"/>, calling appropriate overloads 
        /// in <paramref name="parser"/>
        /// </summary>
        /// <param name="pattern">Pattern to parse</param>
        /// <param name="parser">Parser to call back</param>
        static public void Parse(WildcardPattern pattern, WildcardPatternParser parser)
        {
            parser.BeginWildcardPattern(pattern);

            bool previousCharacterIsAnEscape = false;
            bool previousCharacterStartedBracketExpression = false;
            bool insideCharacterRange = false;
            StringBuilder characterRangeContents = null;
            StringBuilder characterRangeOperators = null;
            foreach (char c in pattern.Pattern)
            {
                if (insideCharacterRange)
                {
                    if (c == ']' && !previousCharacterStartedBracketExpression && !previousCharacterIsAnEscape)
                    {
                        // An unescaped closing square bracket closes the character set.  In other
                        // words, there are no nested square bracket expressions
                        // This is different than the POSIX spec 
                        // (at http://www.opengroup.org/onlinepubs/9699919799/functions/fnmatch.html),
                        // but we are keeping this behavior for back-compatibility.

                        insideCharacterRange = false;
                        parser.AppendBracketExpression(characterRangeContents.ToString(), characterRangeOperators.ToString(), pattern.Pattern);
                        characterRangeContents = null;
                        characterRangeOperators = null;
                    }
                    else if (c != '`' || previousCharacterIsAnEscape)
                    {
                        characterRangeContents.Append(c);
                        characterRangeOperators.Append((c == '-') && !previousCharacterIsAnEscape ? '-' : ' ');
                    }

                    previousCharacterStartedBracketExpression = false;
                }
                else
                {
                    if (c == '*' && !previousCharacterIsAnEscape)
                    {
                        parser.AppendAsterix();
                    }
                    else if (c == '?' && !previousCharacterIsAnEscape)
                    {
                        parser.AppendQuestionMark();
                    }
                    else if (c == '[' && !previousCharacterIsAnEscape)
                    {
                        insideCharacterRange = true;
                        characterRangeContents = new StringBuilder();
                        characterRangeOperators = new StringBuilder();
                        previousCharacterStartedBracketExpression = true;
                    }
                    else if (c != '`' || previousCharacterIsAnEscape)
                    {
                        parser.AppendLiteralCharacter(c);
                    }
                }

                previousCharacterIsAnEscape = (c == '`') && (!previousCharacterIsAnEscape);
            }

            if (insideCharacterRange)
            {
                throw NewWildcardPatternException(pattern.Pattern);
            }

            if (previousCharacterIsAnEscape)
            {
                if (!pattern.Pattern.Equals("`", StringComparison.Ordinal)) // Win7 backcompatibility requires treating '`' pattern as '' pattern
                {
                    parser.AppendLiteralCharacter(pattern.Pattern[pattern.Pattern.Length - 1]);
                }
            }

            parser.EndWildcardPattern();
        }

        internal static WildcardPatternException NewWildcardPatternException(string invalidPattern)
        {
            string message =
                StringUtil.Format(WildcardPatternStrings.InvalidPattern,
                    invalidPattern
                );

            ParentContainsErrorRecordException pce =
                new ParentContainsErrorRecordException(message);

            ErrorRecord er =
                new ErrorRecord( pce,
                                 "WildcardPattern_Invalid",
                                 ErrorCategory.InvalidArgument,
                                 null );

            WildcardPatternException e =
                new WildcardPatternException( er );

            return e;
        }
    };

    /// <summary>
    /// Convert a string with wild cards into its equivalent regex
    /// </summary>
    /// <remarks>
    /// A list of glob patterns and their equivalent regexes
    ///
    ///  glob pattern      regex
    /// -------------     -------
    /// *foo*              foo
    /// foo                ^foo$
    /// foo*bar            ^foo.*bar$
    /// foo`*bar           ^foo\*bar$
    ///
    /// for a more cases see the unit-test file RegexTest.cs
    /// </remarks>
    internal class WildcardPatternToRegexParser : WildcardPatternParser
    {
        private StringBuilder regexPattern;
        private RegexOptions regexOptions;

        private const string regexChars = "()[.?*{}^$+|\\"; // ']' is missing on purpose
        private static bool IsRegexChar(char ch)
        {
            for (int i=0; i<regexChars.Length; i++)
            {
                if (ch == regexChars[i])
                {
                    return true;
                }
            }

            return false;
        }

        internal static RegexOptions TranslateWildcardOptionsIntoRegexOptions(WildcardOptions options)
        {
            RegexOptions regexOptions = RegexOptions.Singleline;

#if !CORECLR // RegexOptions.Compiled is not in CoreCLR
            if ((options & WildcardOptions.Compiled) != 0)
            {
                regexOptions |= RegexOptions.Compiled;
            }
#endif
            if ((options & WildcardOptions.IgnoreCase) != 0)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }
            if ((options & WildcardOptions.CultureInvariant) == WildcardOptions.CultureInvariant)
            {
                regexOptions |= RegexOptions.CultureInvariant;
            }

            return regexOptions;
        }

        protected override void BeginWildcardPattern(WildcardPattern pattern)
        {
            regexPattern = new StringBuilder(pattern.Pattern.Length * 2 + 2);
            regexPattern.Append('^');

            regexOptions = TranslateWildcardOptionsIntoRegexOptions(pattern.Options);
        }

        internal static void AppendLiteralCharacter(StringBuilder regexPattern, char c)
        {
            if (IsRegexChar(c))
            {
                regexPattern.Append('\\');
            }
            regexPattern.Append(c);
        }

        protected override void AppendLiteralCharacter(char c)
        {
            AppendLiteralCharacter(this.regexPattern, c);
        }

        protected override void AppendAsterix()
        {
            regexPattern.Append(".*");
        }

        protected override void AppendQuestionMark()
        {
            regexPattern.Append('.');
        }

        protected override void EndWildcardPattern()
        {
            regexPattern.Append('$');

            // lines below are not strictly necessary and are included to preserve
            // wildcard->regex conversion from PS v1 (i.e. not to break unit tests
            // and not to break backcompatibility).
            string regexPatternString = regexPattern.ToString();
            if (regexPatternString.Equals("^.*$", StringComparison.Ordinal))
            {
                regexPattern.Remove(0, 4);
            }
            else 
            {
                if (regexPatternString.StartsWith("^.*", StringComparison.Ordinal))
                {
                    regexPattern.Remove(0, 3);
                }
                if (regexPatternString.EndsWith(".*$", StringComparison.Ordinal))
                {
                    regexPattern.Remove(regexPattern.Length - 3, 3);
                }
            }
        }

        protected override void BeginBracketExpression()
        {
            regexPattern.Append('[');
        }

        internal static void AppendLiteralCharacterToBracketExpression(StringBuilder regexPattern, char c)
        {
            if (c == '[')
            {
                regexPattern.Append('[');
            }
            else if (c == ']')
            {
                regexPattern.Append(@"\]");
            }
            else if (c == '-')
            {
                regexPattern.Append(@"\x2d");
            }
            else
            {
                AppendLiteralCharacter(regexPattern, c);
            }
        }
        protected override void AppendLiteralCharacterToBracketExpression(char c)
        {
            AppendLiteralCharacterToBracketExpression(this.regexPattern, c);
        }

        internal static void AppendCharacterRangeToBracketExpression(
                        StringBuilder regexPattern,
                        char startOfCharacterRange, 
                        char endOfCharacterRange)
        {
            AppendLiteralCharacterToBracketExpression(regexPattern, startOfCharacterRange);
            regexPattern.Append('-');
            AppendLiteralCharacterToBracketExpression(regexPattern, endOfCharacterRange);
        }

        protected override void AppendCharacterRangeToBracketExpression(
                        char startOfCharacterRange,
                        char endOfCharacterRange)
        {
            AppendCharacterRangeToBracketExpression(this.regexPattern, startOfCharacterRange, endOfCharacterRange);
        }

        protected override void EndBracketExpression()
        {
            regexPattern.Append(']');
        }

        /// <summary>
        /// Parses a <paramref name="wildcardPattern"/> into a <see cref="Regex"/>
        /// </summary>
        /// <param name="wildcardPattern">Wildcard pattern to parse</param>
        /// <returns>Regular expression equivalent to <paramref name="wildcardPattern"/></returns>
        static public Regex Parse(WildcardPattern wildcardPattern)
        {
            WildcardPatternToRegexParser parser = new WildcardPatternToRegexParser();
            WildcardPatternParser.Parse(wildcardPattern, parser);
            try
            {
                return new Regex(parser.regexPattern.ToString(), parser.regexOptions);
            }
            catch(ArgumentException)
            {
                throw WildcardPatternParser.NewWildcardPatternException(wildcardPattern.Pattern);
            }
        }
    }

    internal class WildcardPatternMatcher
    {
        private readonly PatternElement[] _patternElements;
        private readonly CharacterNormalizer _characterNormalizer;

        internal WildcardPatternMatcher(WildcardPattern wildcardPattern)
        {
            this._characterNormalizer = new CharacterNormalizer(wildcardPattern.Options);
            this._patternElements = MyWildcardPatternParser.Parse(
                            wildcardPattern, 
                            this._characterNormalizer);
        }

        internal bool IsMatch(string str)
        {
            // - each state of NFA is represented by (patternPosition, stringPosition) tuple
            //     - state transitions are documented in
            //       ProcessStringCharacter and ProcessEndOfString methods
            // - the algorithm below tries to see if there is a path 
            //   from (0, 0) to (lengthOfPattern, lengthOfString)
            //    - this is a regular graph traversal
            //    - there are O(1) edges per node (at most 2 edges) 
            //      so the whole graph traversal takes O(number of nodes in the graph) =
            //      = O(lengthOfPattern * lengthOfString) time
            //    - for efficient remembering which states have already been visited, 
            //      the traversal goes methodically from beginning to end of the string
            //      therefore requiring only O(lengthOfPattern) memory for remembering
            //      which states have been already visited
            //  - Wikipedia calls this algorithm the "NFA" algorithm at
            //    http://en.wikipedia.org/wiki/Regular_expression#Implementations_and_running_times

            var patternPositionsForCurrentStringPosition = 
                    new PatternPositionsVisitor(this._patternElements.Length);
            patternPositionsForCurrentStringPosition.Add(0);

            var patternPositionsForNextStringPosition = 
                    new PatternPositionsVisitor(this._patternElements.Length);

            for (int currentStringPosition = 0; 
                 currentStringPosition < str.Length; 
                 currentStringPosition++)
            {
                char currentStringCharacter = _characterNormalizer.Normalize(str[currentStringPosition]);
                patternPositionsForCurrentStringPosition.StringPosition = currentStringPosition;
                patternPositionsForNextStringPosition.StringPosition = currentStringPosition + 1;

                int patternPosition;
                while (patternPositionsForCurrentStringPosition.MoveNext(out patternPosition))
                {
                    this._patternElements[patternPosition].ProcessStringCharacter(
                        currentStringCharacter,
                        patternPosition,
                        patternPositionsForCurrentStringPosition,
                        patternPositionsForNextStringPosition);
                }

                // swap patternPositionsForCurrentStringPosition 
                // with patternPositionsForNextStringPosition
                var tmp = patternPositionsForCurrentStringPosition;
                patternPositionsForCurrentStringPosition = patternPositionsForNextStringPosition;
                patternPositionsForNextStringPosition = tmp;
            }

            int patternPosition2;
            while (patternPositionsForCurrentStringPosition.MoveNext(out patternPosition2))
            {
                this._patternElements[patternPosition2].ProcessEndOfString(
                    patternPosition2,
                    patternPositionsForCurrentStringPosition);
            }

            return patternPositionsForCurrentStringPosition.ReachedEndOfPattern;
        }

        private class PatternPositionsVisitor
        {
            private readonly int _lengthOfPattern;

            private readonly int[] _isPatternPositionVisitedMarker;

            private readonly int[] _patternPositionsForFurtherProcessing;
            private int _patternPositionsForFurtherProcessingCount;

            public PatternPositionsVisitor(int lengthOfPattern)
            {
                Dbg.Assert(lengthOfPattern >= 0, "Caller should verify lengthOfPattern >= 0");

                this._lengthOfPattern = lengthOfPattern;

                this._isPatternPositionVisitedMarker = new int[lengthOfPattern + 1];
                for (int i = 0; i < this._isPatternPositionVisitedMarker.Length; i++)
                {
                    this._isPatternPositionVisitedMarker[i] = -1;
                }

                this._patternPositionsForFurtherProcessing = new int[lengthOfPattern];
                this._patternPositionsForFurtherProcessingCount = 0;
            }

            public int StringPosition { private get; set; }

            public void Add(int patternPosition)
            {
                Dbg.Assert(patternPosition >= 0, "Caller should verify patternPosition >= 0");
                Dbg.Assert(
                        patternPosition <= this._lengthOfPattern, 
                        "Caller should verify patternPosition <= this._lengthOfPattern");

                // is patternPosition already visited?);
                if (this._isPatternPositionVisitedMarker[patternPosition] == this.StringPosition)
                {
                    return;
                }

                // mark patternPosition as visited
                this._isPatternPositionVisitedMarker[patternPosition] = this.StringPosition;

                // add patternPosition to the queue for further processing
                if (patternPosition < this._lengthOfPattern)
                {
                    this._patternPositionsForFurtherProcessing[this._patternPositionsForFurtherProcessingCount] = patternPosition;
                    this._patternPositionsForFurtherProcessingCount++;
                    Dbg.Assert(
                            this._patternPositionsForFurtherProcessingCount <= this._lengthOfPattern, 
                            "There should never be more elements in the queue than the length of the pattern");
                }
            }

            public bool ReachedEndOfPattern
            {
                get
                {
                    return this._isPatternPositionVisitedMarker[this._lengthOfPattern] >= this.StringPosition;
                }
            }

            // non-virtual MoveNext is more performant 
            // than implementing IEnumerable / virtual MoveNext 
            public bool MoveNext(out int patternPosition)
            {
                Dbg.Assert(
                        this._patternPositionsForFurtherProcessingCount >= 0, 
                        "There should never be more elements in the queue than the length of the pattern");

                if (this._patternPositionsForFurtherProcessingCount == 0)
                {
                    patternPosition = -1;
                    return false;
                }

                this._patternPositionsForFurtherProcessingCount--;
                patternPosition = this._patternPositionsForFurtherProcessing[this._patternPositionsForFurtherProcessingCount];
                return true;
            }
        }

        private abstract class PatternElement
        {
            public abstract void ProcessStringCharacter(
                            char currentStringCharacter, 
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForCurrentStringPosition, 
                            PatternPositionsVisitor patternPositionsForNextStringPosition);

            public abstract void ProcessEndOfString(
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForEndOfStringPosition);
        }

        private class QuestionMarkElement : PatternElement
        {
            public override void ProcessStringCharacter(
                            char currentStringCharacter, 
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForCurrentStringPosition, 
                            PatternPositionsVisitor patternPositionsForNextStringPosition)
            {
                // '?' : (patternPosition, stringPosition) => (patternPosition + 1, stringPosition + 1)
                patternPositionsForNextStringPosition.Add(currentPatternPosition + 1);
            }

            public override void ProcessEndOfString(
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForEndOfStringPosition)
            {
                // '?' : (patternPosition, endOfString) => <no transitions out of this state - cannot move beyond end of string>
            }
        }

        private class LiteralCharacterElement : QuestionMarkElement
        {
            private readonly char _literalCharacter;

            public LiteralCharacterElement(char literalCharacter)
            {
                this._literalCharacter = literalCharacter;
            }

            public override void ProcessStringCharacter(
                            char currentStringCharacter, 
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForCurrentStringPosition, 
                            PatternPositionsVisitor patternPositionsForNextStringPosition)
            {
                if (this._literalCharacter == currentStringCharacter)
                {
                    base.ProcessStringCharacter(
                            currentStringCharacter, 
                            currentPatternPosition, 
                            patternPositionsForCurrentStringPosition, 
                            patternPositionsForNextStringPosition);
                }
            }
        }

        private class BracketExpressionElement : QuestionMarkElement
        {
            private readonly Regex _regex;

            public BracketExpressionElement(Regex regex)
            {
                Dbg.Assert(regex != null, "Caller should verify regex != null");
                this._regex = regex;
            }

            public override void ProcessStringCharacter(
                            char currentStringCharacter, 
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForCurrentStringPosition, 
                            PatternPositionsVisitor patternPositionsForNextStringPosition)
            {
                if (this._regex.IsMatch(new string(currentStringCharacter, 1)))
                {
                    base.ProcessStringCharacter(currentStringCharacter, currentPatternPosition,
                                                patternPositionsForCurrentStringPosition,
                                                patternPositionsForNextStringPosition);
                }
            }
        }

        private class AsterixElement : PatternElement
        {
            public override void ProcessStringCharacter(
                            char currentStringCharacter, 
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForCurrentStringPosition, 
                            PatternPositionsVisitor patternPositionsForNextStringPosition)
            {
                // '*' : (patternPosition, stringPosition) => (patternPosition + 1, stringPosition)
                patternPositionsForCurrentStringPosition.Add(currentPatternPosition + 1);

                // '*' : (patternPosition, stringPosition) => (patternPosition, stringPosition + 1)
                patternPositionsForNextStringPosition.Add(currentPatternPosition);
            }

            public override void ProcessEndOfString(
                            int currentPatternPosition, 
                            PatternPositionsVisitor patternPositionsForEndOfStringPosition)
            {
                // '*' : (patternPosition, endOfString) => (patternPosition + 1, endOfString)
                patternPositionsForEndOfStringPosition.Add(currentPatternPosition + 1);
            }
        }

        private class MyWildcardPatternParser : WildcardPatternParser
        {
            private readonly List<PatternElement> _patternElements = new List<PatternElement>();
            private CharacterNormalizer _characterNormalizer;
            private RegexOptions _regexOptions;
            private StringBuilder _bracketExpressionBuilder;

            static public PatternElement[] Parse(
                            WildcardPattern pattern, 
                            CharacterNormalizer characterNormalizer)
            {
                var parser = new MyWildcardPatternParser
                    {
                        _characterNormalizer = characterNormalizer,
                        _regexOptions = WildcardPatternToRegexParser.TranslateWildcardOptionsIntoRegexOptions(pattern.Options),
                    }; 
                WildcardPatternParser.Parse(pattern, parser);
                return parser._patternElements.ToArray();
            }

            protected override void AppendLiteralCharacter(char c)
            {
                c = this._characterNormalizer.Normalize(c);
                this._patternElements.Add(new LiteralCharacterElement(c));
            }

            protected override void AppendAsterix()
            {
                this._patternElements.Add(new AsterixElement());
            }

            protected override void AppendQuestionMark()
            {
                this._patternElements.Add(new QuestionMarkElement());
            }

            protected override void BeginBracketExpression()
            {
                this._bracketExpressionBuilder = new StringBuilder();
                this._bracketExpressionBuilder.Append('[');
            }

            protected override void AppendLiteralCharacterToBracketExpression(char c)
            {
                WildcardPatternToRegexParser.AppendLiteralCharacterToBracketExpression(
                    this._bracketExpressionBuilder, 
                    c);
            }

            protected override void AppendCharacterRangeToBracketExpression(
                            char startOfCharacterRange, 
                            char endOfCharacterRange)
            {
                WildcardPatternToRegexParser.AppendCharacterRangeToBracketExpression(
                    this._bracketExpressionBuilder, 
                    startOfCharacterRange, 
                    endOfCharacterRange);
            }

            protected override void EndBracketExpression()
            {
                this._bracketExpressionBuilder.Append(']');
                Regex regex = new Regex(this._bracketExpressionBuilder.ToString(), this._regexOptions);
                this._patternElements.Add(new BracketExpressionElement(regex));
            }
        }

        private struct CharacterNormalizer
        {
            private readonly CultureInfo _cultureInfo;
            private readonly bool _caseInsensitive;

            public CharacterNormalizer(WildcardOptions options)
            {
                _caseInsensitive = 0 != (options & WildcardOptions.IgnoreCase);
                if (_caseInsensitive)
                {
                    _cultureInfo = 0 != (options & WildcardOptions.CultureInvariant)
                        ? CultureInfo.InvariantCulture
                        : CultureInfo.CurrentCulture;
                }
                else
                {
                    // Don't bother saving the culture if we won't use it
                    _cultureInfo = null;
                }
            }

            [Pure]
            public char Normalize(char x)
            {
                if (this._caseInsensitive)
                {
                    return this._cultureInfo.TextInfo.ToLower(x);
                }

                return x;
            }
        }
    }

    /// <summary>
    /// Translates a <see cref="WildcardPattern"/> into a DOS wildcard
    /// </summary>
    internal class WildcardPatternToDosWildcardParser : WildcardPatternParser
    {
        private readonly StringBuilder result = new StringBuilder();

        protected override void AppendLiteralCharacter(char c)
        {
            this.result.Append(c);
        }

        protected override void AppendAsterix()
        {
            result.Append('*');
        }

        protected override void AppendQuestionMark()
        {
            result.Append('?');
        }

        protected override void BeginBracketExpression()
        {
        }

        protected override void AppendLiteralCharacterToBracketExpression(char c)
        {
        }

        protected override void AppendCharacterRangeToBracketExpression(char startOfCharacterRange, char endOfCharacterRange)
        {
        }

        protected override void EndBracketExpression()
        {
            result.Append('?');
        }

        /// <summary>
        /// Converts <paramref name="wildcardPattern"/> into a DOS wildcard
        /// </summary>
        static internal string Parse(WildcardPattern wildcardPattern)
        {
            var parser = new WildcardPatternToDosWildcardParser();
            WildcardPatternParser.Parse(wildcardPattern, parser);
            return parser.result.ToString();
        }
    }
}

