/*
 * KeyValues3 class.
 * This class reads in Valve KV3 files and stores them in its datastructure.
 *
 * Interface:
 *  KVFile file = KV3Reader.ParseKVFile( fileName );
 *  String fileString = file.Serialize();
 *
 * TODO:
 *  - Test some more and find the bugs.
 *  - Revisit state machine if bugs require it.
 *  - Improve KVFile interface.
 *
 * Author: Perry - https://github.com/Perryvw
 *
 */
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KVValueType = ValveKeyValue.KVValueType;

#nullable disable

namespace ValveResourceFormat.Serialization.KeyValues
{
    public static class KeyValues3
    {
        private enum State
        {
            HEADER,
            SEEK_VALUE,
            PROP_NAME,
            PROP_NAME_QUOTED,
            VALUE_STRUCT,
            VALUE_ARRAY,
            VALUE_STRING,
            VALUE_STRING_MULTI,
            VALUE_BINARY_BLOB,
            VALUE_NUMBER,
            VALUE_FLAGGED,
            COMMENT,
            COMMENT_BLOCK
        }

        private class Parser
        {
            public StreamReader FileStream;

            public readonly KVObject Root;

            public string CurrentName;
            public readonly StringBuilder CurrentString;

            public char PreviousChar;
            public readonly Queue<char> CharBuffer;

            public readonly Stack<KVObject> ObjStack;
            public readonly Stack<State> StateStack;

            public bool EndOfStream => FileStream.EndOfStream && CharBuffer.Count == 0;

            public Parser()
            {
                //Initialise datastructures
                ObjStack = new Stack<KVObject>();
                StateStack = new Stack<State>();
                StateStack.Push(State.HEADER);

                Root = new KVObject("root");
                ObjStack.Push(Root);

                PreviousChar = '\0';
                CharBuffer = new Queue<char>();

                CurrentString = new StringBuilder();
            }
        }

        public static KV3File ParseKVFile(string filename)
        {
            using var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            return ParseKVFile(fileStream);
        }

        public static KV3File ParseKVFile(Stream fileStream)
        {
            var parser = new Parser
            {
                FileStream = new StreamReader(fileStream)
            };

            char c;
            while (!parser.EndOfStream)
            {
                c = NextChar(parser);

                if (parser.StateStack.Count == 0)
                {
                    if (!char.IsWhiteSpace(c))
                    {
                        throw new InvalidDataException($"Unexpected character '{c}' at position {parser.FileStream.BaseStream.Position}");
                    }

                    continue;
                }

                //Do something depending on the current state
                switch (parser.StateStack.Peek())
                {
                    case State.HEADER:
                        ReadHeader(c, parser);
                        break;
                    case State.PROP_NAME:
                        ReadPropName(c, parser);
                        break;
                    case State.PROP_NAME_QUOTED:
                        ReadPropNameQuoted(c, parser);
                        break;
                    case State.SEEK_VALUE:
                        SeekValue(c, parser);
                        break;
                    case State.VALUE_STRUCT:
                        ReadValueStruct(c, parser);
                        break;
                    case State.VALUE_STRING:
                        ReadValueString(c, parser);
                        break;
                    case State.VALUE_STRING_MULTI:
                        ReadValueStringMulti(c, parser);
                        break;
                    case State.VALUE_BINARY_BLOB:
                        ReadValueBinaryBlob(c, parser);
                        break;
                    case State.VALUE_NUMBER:
                        ReadValueNumber(c, parser);
                        break;
                    case State.VALUE_ARRAY:
                        ReadValueArray(c, parser);
                        break;
                    case State.VALUE_FLAGGED:
                        ReadValueFlagged(c, parser);
                        break;
                    case State.COMMENT:
                        ReadComment(c, parser);
                        break;
                    case State.COMMENT_BLOCK:
                        ReadCommentBlock(c, parser);
                        break;
                }

                parser.PreviousChar = c;
            }

            return new KV3File((KVObject)parser.Root.Properties.ElementAt(0).Value.Value); //TODO: give Encoding and Formatting too
        }

        //header state
        private static void ReadHeader(char c, Parser parser)
        {
            parser.CurrentString.Append(c);

            //Read until --> is encountered
            if (c == '>' && parser.CurrentString.ToString()[(parser.CurrentString.Length - 3)..] == "-->")
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.SEEK_VALUE);
                return;
            }
        }

        //Seeking value state
        private static void SeekValue(char c, Parser parser)
        {
            //Ignore whitespace
            if (char.IsWhiteSpace(c) || c == '=')
            {
                return;
            }

            //Check struct opening
            if (c == '{')
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.VALUE_STRUCT);

                parser.ObjStack.Push(new KVObject(parser.CurrentString.ToString()));
            }

            //Check for array opening
            else if (c == '[')
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.VALUE_ARRAY);

                parser.ObjStack.Push(new KVObject(parser.CurrentString.ToString(), true));
            }

            //Check for array closing
            else if (c == ']')
            {
                parser.StateStack.Pop();
                parser.StateStack.Pop();

                var value = parser.ObjStack.Pop();
                parser.ObjStack.Peek().AddProperty(value.Key, new KVValue(KVValueType.Array, value));
            }

            //String opening
            else if (c == '"')
            {
                //Check if a multistring or single string was found
                var next = PeekString(parser, 4);
                if (next.Contains("\"\"\n", StringComparison.Ordinal) || next == "\"\"\r\n")
                {
                    //Skip the next two "'s
                    SkipChars(parser, 2);

                    parser.StateStack.Pop();
                    parser.StateStack.Push(State.VALUE_STRING_MULTI);
                    parser.CurrentString.Clear();
                }
                else
                {
                    parser.StateStack.Pop();
                    parser.StateStack.Push(State.VALUE_STRING);
                    parser.CurrentString.Clear();
                }
            }

            // Binary Blob
            else if (ReadAheadMatches(parser, c, "#["))
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.VALUE_BINARY_BLOB);
                parser.CurrentString.Clear();

                //Skip next characters
                SkipChars(parser, "#[".Length - 1);
            }

            //Boolean false
            else if (ReadAheadMatches(parser, c, "false"))
            {
                parser.StateStack.Pop();

                //Can directly be added
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.Boolean, false));

                //Skip next characters
                SkipChars(parser, "false".Length - 1);
            }

            //Boolean true
            else if (ReadAheadMatches(parser, c, "true"))
            {
                parser.StateStack.Pop();

                //Can directly be added
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.Boolean, true));

                //Skip next characters
                SkipChars(parser, "true".Length - 1);
            }

            //Null
            else if (ReadAheadMatches(parser, c, "null"))
            {
                parser.StateStack.Pop();

                //Can directly be added
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, null);

                //Skip next characters
                SkipChars(parser, "null".Length - 1);
            }

            // Number
            else if (ReadAheadIsNumber(parser, c))
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.VALUE_NUMBER);
                parser.CurrentString.Clear();
                parser.CurrentString.Append(c);
            }

            //Flagged resource
            else
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.VALUE_FLAGGED);
                parser.CurrentString.Clear();
                parser.CurrentString.Append(c);
            }
        }

        //Reading a property name
        private static void ReadPropName(char c, Parser parser)
        {
            //Stop once whitespace is encountered
            if (char.IsWhiteSpace(c))
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.SEEK_VALUE);
                parser.CurrentName = parser.CurrentString.ToString();
                return;
            }

            parser.CurrentString.Append(c);
        }

        //Reading a quoted property name
        private static void ReadPropNameQuoted(char c, Parser parser)
        {
            if (c == '"' && parser.PreviousChar != '\\')
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.SEEK_VALUE);
                parser.CurrentName = parser.CurrentString.ToString();
                return;
            }

            parser.CurrentString.Append(c);
        }

        //Read a structure
        private static void ReadValueStruct(char c, Parser parser)
        {
            //Ignore whitespace
            if (char.IsWhiteSpace(c))
            {
                return;
            }

            //Catch comments
            if (c == '/')
            {
                parser.StateStack.Push(State.COMMENT);
                parser.CurrentString.Clear();
                parser.CurrentString.Append(c);
                return;
            }

            //Check for the end of the structure
            if (c == '}')
            {
                var value = parser.ObjStack.Pop();
                parser.ObjStack.Peek().AddProperty(value.Key, new KVValue(KVValueType.Collection, value));
                parser.StateStack.Pop();
                return;
            }

            //Start looking for the next property name

            parser.CurrentString.Clear();

            if (c == '"')
            {
                parser.StateStack.Push(State.PROP_NAME_QUOTED);
            }
            else
            {
                parser.StateStack.Push(State.PROP_NAME);
                parser.CurrentString.Append(c);
            }
        }

        //Read a string value
        private static void ReadValueString(char c, Parser parser)
        {
            if (c == '"' && parser.PreviousChar != '\\')
            {
                //String ending found
                parser.StateStack.Pop();
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.String, parser.CurrentString.ToString()));
                return;
            }

            parser.CurrentString.Append(c);
        }

        //Reading multiline string
        private static void ReadValueStringMulti(char c, Parser parser)
        {
            //Check for ending
            var next = PeekString(parser, 2);
            if (c == '"' && next == "\"\"" && parser.PreviousChar != '\\')
            {
                //Check for starting and trailing linebreaks
                var multilineStr = parser.CurrentString.ToString();
                var start = 0;
                var end = multilineStr.Length;

                //Check the start
                if (multilineStr.ElementAt(0) == '\n')
                {
                    start = 1;
                }
                else if (multilineStr.ElementAt(0) == '\r' && multilineStr.ElementAt(1) == '\n')
                {
                    start = 2;
                }

                //Check the end
                if (multilineStr.ElementAt(multilineStr.Length - 1) == '\n')
                {
                    if (multilineStr.ElementAt(multilineStr.Length - 2) == '\r')
                    {
                        end = multilineStr.Length - 2;
                    }
                    else
                    {
                        end = multilineStr.Length - 1;
                    }
                }

                multilineStr = multilineStr[start..end];

                //Set parser state
                parser.StateStack.Pop();
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.String, multilineStr));

                //Skip to end of the block
                SkipChars(parser, 2);
                return;
            }

            parser.CurrentString.Append(c);
        }

        // Read binary blob
        private static void ReadValueBinaryBlob(char c, Parser parser)
        {
            if (c == ']')
            {
                // binary blod ending
                parser.StateStack.Pop();
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.BinaryBlob, Convert.FromHexString(parser.CurrentString.ToString())));
            }
            else if (!char.IsWhiteSpace(c))
            {
                parser.CurrentString.Append(c);
            }
        }

        //Read a numerical value
        private static void ReadValueNumber(char c, Parser parser)
        {
            //Stop reading the number once whitespace (or comma in arrays) is encountered
            if (char.IsWhiteSpace(c) || c == ',')
            {
                //Distinguish between doubles and ints
                parser.StateStack.Pop();
                if (parser.CurrentString.ToString().Contains('.', StringComparison.Ordinal))
                {
                    parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.FloatingPoint64, double.Parse(parser.CurrentString.ToString(), CultureInfo.InvariantCulture)));
                }
                else
                {
                    parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.Int64, long.Parse(parser.CurrentString.ToString(), CultureInfo.InvariantCulture)));
                }

                return;
            }

            parser.CurrentString.Append(c);
        }

        //Read an array
        private static void ReadValueArray(char c, Parser parser)
        {
            //Check for the end of the array
            if (c == ']')
            {
                var value = parser.ObjStack.Pop();
                parser.ObjStack.Peek().AddProperty(value.Key, new KVValue(KVValueType.Array, value));
                parser.StateStack.Pop();
                return;
            }

            //This shouldn't happen
            if (!char.IsWhiteSpace(c) && c != ',')
            {
                throw new InvalidDataException("Error in array format.");
            }

            //Just jump to seek_value state
            parser.StateStack.Push(State.SEEK_VALUE);
        }

        //Read a flagged value
        private static void ReadValueFlagged(char c, Parser parser)
        {
            //End at whitespace
            if (char.IsWhiteSpace(c))
            {
                parser.StateStack.Pop();
                var strings = parser.CurrentString.ToString().Split([':'], 2);
                var flag = strings[0] switch
                {
                    "resource" => KVFlag.Resource,
                    "resource_name" => KVFlag.ResourceName,
                    "panorama" => KVFlag.Panorama,
                    "soundevent" => KVFlag.SoundEvent,
                    "subclass" => KVFlag.SubClass,
                    _ => throw new InvalidDataException("Unknown flag " + strings[0]),
                };
                //If flagged value is in the array, it needs to include a comma
                var end = parser.StateStack.Peek() == State.VALUE_ARRAY ? 2 : 1;
                parser.ObjStack.Peek().AddProperty(parser.CurrentName, new KVValue(KVValueType.String, flag, strings[1][1..^end]));
                return;
            }

            parser.CurrentString.Append(c);
        }

        //Read comments
        private static void ReadComment(char c, Parser parser)
        {
            //Check for multiline comments
            if (parser.CurrentString.Length == 1 && c == '*')
            {
                parser.StateStack.Pop();
                parser.StateStack.Push(State.COMMENT_BLOCK);
            }

            //Check for the end of a comment
            if (c == '\n')
            {
                parser.StateStack.Pop();
                return;
            }

            if (c != '\r')
            {
                parser.CurrentString.Append(c);
            }
        }

        //Read a comment block
        private static void ReadCommentBlock(char c, Parser parser)
        {
            //Look for the end of the comment block
            if (c == '/' && parser.CurrentString.ToString().Last() == '*')
            {
                parser.StateStack.Pop();
            }

            parser.CurrentString.Append(c);
        }

        //Get the next char from the filestream
        private static char NextChar(Parser parser)
        {
            //Check if there are characters in the buffer, otherwise read a new one
            if (parser.CharBuffer.Count > 0)
            {
                return parser.CharBuffer.Dequeue();
            }
            else
            {
                return (char)parser.FileStream.Read();
            }
        }

        //Skip the next X characters in the filestream
        private static void SkipChars(Parser parser, int num)
        {
            for (var i = 0; i < num; i++)
            {
                NextChar(parser);
            }
        }

        //Utility function
        private static string PeekString(Parser parser, int length)
        {
            var buffer = new char[length];
            for (var i = 0; i < length; i++)
            {
                if (i < parser.CharBuffer.Count)
                {
                    buffer[i] = parser.CharBuffer.ElementAt(i);
                }
                else
                {
                    buffer[i] = (char)parser.FileStream.Read();
                    parser.CharBuffer.Enqueue(buffer[i]);
                }
            }

            return string.Join(string.Empty, buffer);
        }

        private static bool ReadAheadMatches(Parser parser, char c, string pattern)
        {
            if (c + PeekString(parser, pattern.Length - 1) == pattern)
            {
                return true;
            }

            return false;
        }

        private static bool ReadAheadIsNumber(Parser parser, char c)
        {
            if (char.IsDigit(c))
            {
                return true;
            }

            if (c == '-')
            {
                var nextChar = PeekString(parser, 1);

                return char.IsDigit(nextChar[0]);
            }

            return false;
        }
    }
}
