using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CPreprocessor
{
    using Macros = Dictionary<string, Macro>;
    class ExecutionState
    {
        // copy from RISCVAsm, not use
    }
    delegate dynamic ExecutionFunc(ExecutionState state, bool left = false); // left is always be false
    static class SlashGluer
    {
        public static string Glue(string text)
        {
            var lines = text.Split('\n');
            var ret = new List<string>();
            long cnt = 0;
            string cur = null;
            foreach (var line in lines)
            {
                string rTrim = line.TrimEnd();
                if (rTrim.EndsWith('\\'))
                {
                    string append = rTrim[0..^1];
                    if (cnt > 0)
                    {
                        cur += append;
                    }
                    else
                    {
                        cur = append;
                    }
                    cnt++;
                }
                else
                {
                    if (cnt > 0)
                    {
                        cur += line;
                        ret.Add(cur);
                        cur = null;
                        while (cnt-- > 0)
                        {
                            ret.Add("");
                        }
                        cnt = 0;
                    }
                    else
                    {
                        ret.Add(line);
                    }
                }
            }
            return string.Join('\n', ret);
        }
    }
    class PreprocessorException : Exception
    {
        public bool Matched = false;
        public PreprocessorException(string Reason) : base(Reason) { }
        public PreprocessorException(string Reason, bool Matched) : base(Reason) { this.Matched = Matched; }
    }
    class Macro
    {
        public string Name;
        public List<string> Args = null;
        public bool IsFunc
        {
            get => Args != null;
        }
        public List<Token> Tokens;
        
        public void Apply(TextWriter result, List<string> arg, Macros macros)
        {
            new MacroApplier(macros).Apply(this, result, arg);
        }

        public void Apply(TextWriter result, Macros macros)
        {
            new MacroApplier(macros).Apply(this, result);
        }
    }
    class MacroApplier
    {
        private Macros macros;
        public MacroApplier(Macros macros)
        {
            this.macros = macros;
        }
        private string IdToStr(string id, Dictionary<string, string> param)
        {
            if (param.ContainsKey(id)) return param[id];
            return id;
        }
        private HashSet<string> applied = new HashSet<string>();
        private string ApplyInternal(Macro macro, Dictionary<string, string> param)
        {
            applied.Add(macro.Name);
            StringWriter writer = new StringWriter();
            foreach (var tk in macro.Tokens)
            {
                if (tk.Type == TokenType.ID)
                {
                    string r = IdToStr(tk.Str, param);
                    if(tk.Combination != null)
                    {
                        foreach(var x in tk.Combination)
                        {
                            r += IdToStr(x, param);
                        }
                    }
                    writer.Write(r);
                }
                else if (tk.IsKeyWord("#"))
                {
                    writer.Write('"');
                    writer.Write(tk.Arg);
                    writer.Write('"');
                }
                else if (tk.IsKeyWord("#@"))
                {
                    writer.Write('\'');
                    writer.Write(tk.Arg);
                    writer.Write('\'');
                }
                else writer.Write(tk.Str);
            }
            writer.Flush();
            string tmp = writer.ToString();
            writer.Close();
            var rr = ApplyForString(tmp);
            applied.Remove(macro.Name);
            return rr;
        }
        public string ApplyForString(string str)
        {
            var tokenizerBase = new TextTokenizer(str, "<macro>", 1);
            tokenizerBase.RecHash = false;
            var tokenizer = new ReadOverTokenizer(tokenizerBase);
            StringWriter writer = new StringWriter();
            while (tokenizer.Peek() != null)
            {
                Token info = tokenizer.Read();
                if (info.Type != TokenType.ID)
                {
                    writer.Write(info.Str);
                }
                else
                {
                    if (!macros.ContainsKey(info.Str) || applied.Contains(info.Str))
                    {
                        writer.Write(info.Str);
                    }
                    else
                    {
                        var mac = macros[info.Str];
                        if (mac.IsFunc)
                        {
                            if (info.IsCall)
                            {
                                while (!tokenizer.Read().IsKeyWord("(")) ;
                                int dep = 0;
                                List<string> arg = new List<string>();
                                string cur = null;
                                while (true)
                                {
                                    var tk = tokenizer.Read();
                                    if (tk == null)
                                    {
                                        throw new PreprocessorException($"macro function not end");
                                    }
                                    if (tk.IsKeyWord("(")) dep++;
                                    if (tk.IsKeyWord(")"))
                                    {
                                        dep--;
                                        if (dep < 0) break;
                                    }
                                    if (dep == 0 && tk.Type == TokenType.Plain && tk.Str == ",")
                                    {
                                        arg.Add(cur);
                                        cur = null;
                                    }
                                    else
                                    {
                                        if (cur == null) cur = tk.Str;
                                        else cur += tk.Str;
                                    }
                                }
                                if (cur != null) arg.Add(cur);
                                Apply(mac, writer, arg);
                            }
                            else
                            {
                                writer.Write(info.Str);
                            }
                        }
                        else
                        {
                            Apply(mac, writer);
                        }
                    }
                }
            }
            writer.Flush();
            string rr = writer.ToString();
            writer.Close();
            return rr;
        }
        public void Apply(Macro macro, TextWriter result, List<string> arg)
        {
            if (macro.Args.Count > 0 && macro.Args.Last() == "...")
            {
                if (macro.Args.Count - 1 > arg.Count)
                {
                    throw new PreprocessorException($"require at least {macro.Args.Count - 1} arguments, but {arg.Count} provided");
                }
                Dictionary<string, string> parami = new Dictionary<string, string>();
                for (int i = 0; i < macro.Args.Count - 1; i++)
                {
                    parami[macro.Args[i]] = arg[i];
                }
                parami["__VA_ARGS__"] = string.Join(",", arg.Skip(macro.Args.Count - 1));
                result.Write(ApplyInternal(macro, parami));
                return;
            }
            if (macro.Args.Count != arg.Count)
            {
                throw new PreprocessorException($"require {macro.Args.Count} arguments, but {arg.Count} provided");
            }
            Dictionary<string, string> param = new Dictionary<string, string>();
            for (int i = 0; i < arg.Count; i++)
            {
                param[macro.Args[i]] = arg[i];
            }
            result.Write(ApplyInternal(macro, param));
        }

        public void Apply(Macro macro, TextWriter result)
        {
            result.Write(ApplyInternal(macro, new Dictionary<string, string>()));
        }
    }
    static class IdReader
    {
        public static bool IsIdStart(char chr)
        {
            return char.IsLetter(chr) || "_$".Contains(chr);
        }
        public static bool IsIdBody(char chr)
        {
            return char.IsDigit(chr) || IsIdStart(chr);
        }
        public static bool ReadId(ref string str, out string id)
        {
            if (str.Length == 0 || !IsIdStart(str[0]))
            {
                id = null;
                return false;
            }
            int len = 1;
            while (str.Length > len && IsIdBody(str[len])) len++;
            id = str[..len];
            str = str[len..];
            return true;
        }
        public static bool IsId(string id)
        {
            if (id.Length == 0 || !IsIdStart(id[0])) return false;
            return !id.Skip(1).Any(x => !IsIdBody(x));
        }
    }
    class Executor
    {
        private HashSet<string> included = new HashSet<string>();
        private List<string> opened = new List<string>();
        private HashSet<string> once = new HashSet<string>();
        private static ConcurrentDictionary<string, string> fileTextCache = new ConcurrentDictionary<string, string>();
        public string GetIncludeFileName(string name, bool isStd = false)
        {
            if (!isStd)
            {
                foreach(var nn in opened.Reverse<string>())
                {
                    var path = Path.GetDirectoryName(nn);
                    if (File.Exists(Path.Combine(path, name))) return Path.Combine(path, name);
                }
            }
            foreach(var path in Program.Includes)
            {
                if (File.Exists(Path.Combine(path, name))) return Path.Combine(path, name);
            }
            throw new PreprocessorException($"{name} for isStd({isStd}) is not found");
        }
        private Tokenizer ParseInternal(string text, string fileName)
        {
            text = SlashGluer.Glue(text);
            Tokenizer tokenizer = new TextTokenizer(text, fileName, 1);
            var sb = new StringBuilder();
            // remove comment
            while (tokenizer.Peek() != null)
            {
                sb.Append(tokenizer.Read().Str);
            }
            var tokenizerBase = new TextTokenizer(sb.ToString(), fileName, 1);
            tokenizerBase.RecHash = true;
            tokenizer = new ReadOverTokenizer(tokenizerBase);
            return tokenizer;
        }
        public Tokenizer ParseFile(string fileName, bool includedCheck = false)
        {
            fileName = Path.GetFullPath(fileName);
            if (includedCheck && included.Contains(fileName)) return null;
            if (fileTextCache.ContainsKey(fileName))
            {

                return ParseInternal(fileTextCache[fileName], Path.GetFileName(fileName));
            }
            else
            {
                if (!File.Exists(fileName)) throw new PreprocessorException($"reader: file({fileName}) not found");
                included.Add(fileName);
                var text = File.ReadAllText(fileName);
                fileTextCache[fileName] = text;
                return ParseInternal(text, Path.GetFileName(fileName));
            }
        }
        
        private class IfState
        {
            public bool emit = false;
            public bool emitToEndIf = false;
            public Token latest = null;
        }
        private TextWriter result;
        private Stack<IfState> chain;
        private Macros macro = new Macros();
        private static HashSet<string> ifs = new HashSet<string>()
        {
            "if", "ifdef", "ifndef"
        };
        private void passLines(Token token)
        {
            for (long i = 0; i < token.Lines; i++)
            {
                result.WriteLine();
            }
        }
        private string TopFileName = "";
        public void Execute(string fileName, bool includedCheck = false)
        {
            Program.Show(ShowLevel.Detail, $"process {fileName}");
            string curMatchName = Path.GetDirectoryName(TopFileName) == Path.GetDirectoryName(fileName) ? Path.GetFileName(fileName) : Path.GetFullPath(fileName);
            PreDefine("__FILE__", curMatchName);
            result.WriteLine($"# 1 \"{curMatchName}\"");
            var tokenizer = ParseFile(fileName, includedCheck);
            if (tokenizer == null) return;
            opened.Add(fileName);
            while (tokenizer.Peek() != null)
            {
                var info = tokenizer.Read();
                try
                {
                    if (info.Directive != null)
                    {
                        if (info.Directive.Type != "line" && (info.Directive.Type != "pragma" || info.Directive.Remain != "once"))
                            passLines(info);
                        if (ifs.Contains(info.Directive.Type))
                        {
                            chain.Push(new IfState() { latest = info });
                            bool ret = false;
                            if (info.Directive.Type == "ifdef")
                            {
                                if (!IdReader.IsId(info.Directive.Remain))
                                {
                                    throw new PreprocessorException("marco name is not well-formed");
                                }
                                ret = macro.ContainsKey(info.Directive.Remain);
                            }
                            else if(info.Directive.Type == "ifndef")
                            {
                                if (!IdReader.IsId(info.Directive.Remain))
                                {
                                    throw new PreprocessorException("marco name is not well-formed");
                                }
                                ret = !macro.ContainsKey(info.Directive.Remain);
                            }
                            else
                            {
                                ret = info.Directive.EvalCond(macro);
                            }
                            if (!ret) chain.Peek().emit = true;
                            continue;
                        }
                        else if (info.Directive.Type == "elif")
                        {
                            if (chain.Count == 0) throw new PreprocessorException("#elif founded no #if ahead");
                            chain.Peek().latest = info;
                            if (!chain.Peek().emit)
                            {
                                chain.Peek().emit = false;
                                chain.Peek().emitToEndIf = true;
                            }
                            if (chain.Peek().emitToEndIf) continue;
                            chain.Peek().emit = !info.Directive.EvalCond(macro);
                            continue;
                        }
                        else if (info.Directive.Type == "else")
                        {
                            if (chain.Count == 0) throw new PreprocessorException("#elif founded no #if ahead");
                            chain.Peek().latest = info;
                            if (!chain.Peek().emit)
                            {
                                chain.Peek().emit = false;
                                chain.Peek().emitToEndIf = true;
                            }
                            if (chain.Peek().emitToEndIf) continue;
                            chain.Peek().emit = false;
                            continue;
                        }
                        else if (info.Directive.Type == "endif")
                        {
                            if (chain.Count == 0) throw new PreprocessorException("#else founded no #if ahead");
                            chain.Pop();
                            continue;
                        }
                    }
                    if (chain.Count > 0)
                        if (chain.Peek().emit || chain.Peek().emitToEndIf)
                        {
                            if (info.Directive == null) passLines(info);
                            continue;
                        }
                    if (info.Directive != null)
                    {
                        if (info.Directive.Type == "pragma")
                        {
                            if(info.Directive.Remain == "once")
                            {
                                string full = Path.GetFullPath(fileName);
                                if (once.Contains(full))
                                    break;
                                once.Add(full);
                            }
                            else
                            {
                                result.Write(info.Str);
                            }
                        }
                        else if (info.Directive.Type == "line" || info.Directive.IsNull)
                        {
                            result.Write(info.Str);
                        }
                        else if (info.Directive.Type == "include" || info.Directive.Type == "include_once")
                        {
                            bool isStd;
                            string val = info.Directive.Remain;
                            if (val.StartsWith("<") && val.EndsWith(">"))
                            {
                                isStd = true;
                            }
                            else if (val.StartsWith("\"") && val.EndsWith("\""))
                            {
                                isStd = false;
                            }
                            else
                            {
                                val = new MacroApplier(macro).ApplyForString(info.Directive.Remain).Trim();
                                if (val.StartsWith("<") && val.EndsWith(">"))
                                {
                                    isStd = true;
                                }
                                else if (val.StartsWith("\"") && val.EndsWith("\""))
                                {
                                    isStd = false;
                                }
                                else
                                    throw new PreprocessorException("include should have \"name\" or <name> form");
                            }
                            Execute(GetIncludeFileName(val[1..^1].Trim(), isStd), info.Directive.Type == "include_once");
                            result.WriteLine();
                            result.WriteLine($"# {info.LineNo + info.Lines} \"{curMatchName}\"");
                            PreDefine("__FILE__", curMatchName);
                        }
                        else if (info.Directive.Type == "error")
                        {
                            throw new PreprocessorException($"assert: {info.Directive.Remain}");
                        }
                        else if (info.Directive.Type == "undef")
                        {
                            if (!IdReader.IsId(info.Directive.Remain))
                            {
                                throw new PreprocessorException("marco name is not well-formed");
                            }
                            macro.Remove(info.Directive.Remain);
                        }
                        else if (info.Directive.Type == "define")
                        {
                            string re = info.Directive.Remain;
                            if (!IdReader.ReadId(ref re, out string name))
                            {
                                throw new PreprocessorException($"define name not exist");
                            }
                            if (macro.ContainsKey(name) || name == "__LINE__")
                            {
                                Program.Show(ShowLevel.Warning, $"marco {name} redefined");
                            }
                            List<string> arg = null;
                            if (re.StartsWith("("))
                            {
                                var sps = re[1..].Split(')', 2);
                                re = sps[1];
                                if (sps[0].Trim().Length == 0)
                                {
                                    arg = new List<string>();
                                }
                                else arg = sps[0].Split(',').Select(x => x.Trim()).ToList();
                                if (arg.Any(x => !IdReader.IsId(x) && x != "...") || arg.SkipLast(1).Any(x => x == "..."))
                                {
                                    throw new PreprocessorException($"marco arguments name is not well-formed");
                                }
                            }
                            macro[name] = new Macro()
                            {
                                Name = name,
                                Args = arg,
                                Tokens = new MacroRuleParse(new ReadOverTokenizer(new MacroRuleTokenizer(info, re.Trim()))).Parse()
                            };
                        }
                        else
                        {
                            throw new PreprocessorException($"{info.Type} is unknown type for preprocessor");
                        }
                    }
                    else
                    {
                        if (info.Type != TokenType.ID)
                        {
                            result.Write(info.Str);
                        }
                        else
                        {
                            if (!macro.ContainsKey(info.Str))
                            {
                                if (info.Str == "__LINE__")
                                {
                                    result.Write(info.LineNo);
                                } else result.Write(info.Str);
                            }
                            else
                            {
                                var mac = macro[info.Str];
                                if (mac.IsFunc)
                                {
                                    if (info.IsCall)
                                    {
                                        while (!tokenizer.Read().IsKeyWord("(")) ;
                                        int dep = 0;
                                        List<string> arg = new List<string>();
                                        string cur = null;
                                        while (true)
                                        {
                                            var tk = tokenizer.Read();
                                            if (tk == null)
                                            {
                                                throw new PreprocessorException($"macro function not end");
                                            }
                                            if (tk.IsKeyWord("(")) dep++;
                                            if (tk.IsKeyWord(")"))
                                            {
                                                dep--;
                                                if (dep < 0) break;
                                            }
                                            if (dep == 0 && tk.Type == TokenType.Plain && tk.Str == ",")
                                            {
                                                arg.Add(cur);
                                                cur = null;
                                            }
                                            else
                                            {
                                                if (cur == null) cur = tk.Str;
                                                else cur += tk.Str;
                                            }
                                        }
                                        if (cur != null) arg.Add(cur);
                                        mac.Apply(result, arg, macro);
                                    }
                                    else
                                    {
                                        result.Write(info.Str);
                                    }
                                }
                                else
                                {
                                    mac.Apply(result, macro);
                                }
                            }
                        }
                    }
                }
                catch (PreprocessorException err)
                {
                    if (err.Matched) throw;
                    throw new PreprocessorException($"{curMatchName}:{info.LineNo}: preprocessor: {err.Message}", true);
                }
            }
            opened.RemoveAt(opened.Count - 1);
        }
        private void PreDefine(string name, string value)
        {
            if (macro.ContainsKey(name)) macro.Remove(name);
            macro.Add(name, new Macro()
            {
                Name = name,
                Args = null,
                Tokens = new List<Token>()
                {
                    new Token() {
                        Type = TokenType.Plain,
                        Str = value
                    }
                }
            });
        }
        public byte[] ExecuteTop(string fileName)
        {
            PreDefine("__STDC_NO_ATOMICS__", "1");
            PreDefine("__STDC_NO_COMPLEX__", "1");
            PreDefine("__STDC_NO_THREADS__", "1");
            PreDefine("__STDC_NO_VLA__", "1");
            PreDefine("__STDC_VERSION__", "199901L");
            PreDefine("__DATE__", DateTime.Now.ToString("MMM dd yyyy", CultureInfo.CreateSpecificCulture("en-US")));
            PreDefine("__TIME__", DateTime.Now.ToString("hh:mm:ss"));
            TopFileName = fileName;
            var @base = new MemoryStream();
            result = new StreamWriter(@base);
            chain = new Stack<IfState>();
            Execute(fileName);
            if (chain.Count > 0)
            {
                var info = chain.Pop().latest;
                throw new PreprocessorException($"{info.FileName}:{info.LineNo}: preprocessor: {info.Str} no corresponding end", true);
            }
            result.Flush();
            byte[] rr = @base.ToArray();
            result.Close();
            return rr;
        }
    }
    enum TokenType
    {
        ID,
        Comment,
        Float,
        Int,
        Keywords,
        String,
        Plain,
        Directive
    }
    class Directive
    {
        public static HashSet<string> Types = new HashSet<string>()
        {
            "if", "else", "endif", "elif", "ifdef", "ifndef", "include", "define", "error", "line", "pragma", "include_once", "undef"
        };
        public string Origin;
        public string Type;
        public string Remain;
        public bool IsNull = false;
        public Directive(string origin)
        {
            origin = origin.Trim();
            Origin = origin;
            if (origin == "")
            {
                IsNull = true;
                return;
            }
            if (!IdReader.ReadId(ref origin, out Type))
            {
                throw new PreprocessorException($"{Origin} not start by a ID");
            }
            Remain = origin.Trim();
            if (!Types.Contains(Type))
            {
                throw new PreprocessorException($"{Type} is unknown");
            }
        }

        public bool EvalCond(Macros macro)
        {
            var ss = new StringWriter();
            var tokenizerBase = new TextTokenizer(Remain, "<macro cond>", 1);
            tokenizerBase.RecHash = false;
            var tokenizer = new ReadOverTokenizer(new TextTokenizer(Remain, "<macro cond>", 1));
            while (tokenizer.Peek() != null)
            {
                var tk = tokenizer.Read();
                if (tk.Type == TokenType.ID && tk.Str == "defined")
                {
                    bool res;
                    if (tk.IsCall)
                    {
                        while (!tokenizer.ReadKeyWords("(")) ;
                        while (true)
                        {
                            var tmp = tokenizer.Read();
                            if (tmp == null)
                            {
                                throw new PreprocessorException("no ID after defined");
                            }
                            if (tmp.IsEmpty()) continue;
                            if (tmp.Type == TokenType.ID)
                            {
                                res = macro.ContainsKey(tmp.Str);
                                break;
                            }
                            throw new PreprocessorException("shall be ID after defined");
                        }
                        while (true)
                        {
                            var tmp = tokenizer.Read();
                            if (tmp == null)
                            {
                                throw new PreprocessorException("no ) for defined");
                            }
                            if (tmp.IsEmpty()) continue;
                            if (tmp.IsKeyWord(")")) break;
                            throw new PreprocessorException("shall be ) after defined");
                        }
                    }
                    else
                    {
                        while (true)
                        {
                            var tmp = tokenizer.Read();
                            if (tmp == null)
                            {
                                throw new PreprocessorException("no ID after defined");
                            }
                            if (tmp.IsEmpty()) continue;
                            if (tmp.Type == TokenType.ID)
                            {
                                res = macro.ContainsKey(tmp.Str);
                                break;
                            }
                            throw new PreprocessorException("shall be ID after defined");
                        }
                    }
                    ss.Write(res ? "(1)" : "(0)");
                }
                else ss.Write(tk.Str);
            }
            var stage1 = ss.ToString();
            ss.Close();
            Program.Show(ShowLevel.Verbose, $"cond after defined process: {stage1}");
            var stage2 = new MacroApplier(macro).ApplyForString(stage1);
            Program.Show(ShowLevel.Verbose, $"cond after macro process: {stage2}");
            // cond parse
            var parse = new CondParser(stage2);
            var func = parse.Parse();
            var result = func(new ExecutionState());
            bool cond;
            if (result == null)
            {
                Program.Show(ShowLevel.Verbose, $"cond result == null");
                cond = false;
            }
            else if (result is long u)
            {
                cond = u != 0;
            }
            else if(result is ulong uu)
            {
                cond = uu != 0;
            }
            else
            {
                throw new PreprocessorException($"unexcept return type {((object)result).GetType().Name}");
            }
            Program.Show(ShowLevel.Verbose, $"cond result: {cond}");
            return cond;
        }
    }
    class Token
    {
        public TokenType Type;
        public bool IsCall = false;
        public string Str;
        public dynamic Parsed = null;
        public Directive Directive = null;
        public string FileName = "(unknown)";
        public long LineNo = 0;
        public long Lines = 0;
        public List<string> Combination = null;
        public string Arg;
        public bool IsEmpty()
        {
            return Type == TokenType.Plain && Str.Trim().Length == 0;
        }
        public bool IsKeyWord(string keyWords)
        {
            return Type == TokenType.Keywords && keyWords == Str;
        }
    }
    interface Tokenizer
    {
        public Token Peek();
        public bool ReadKeyWords(string keyWords);
        public Token Read();
    }
    class ReadOverTokenizer: Tokenizer
    {
        private List<Token> tokens = new List<Token>();
        private int idx = 0;

        public ReadOverTokenizer(Tokenizer from)
        {
            while (from.Peek() != null)
            {
                tokens.Add(from.Read());
            }
            Program.Show(ShowLevel.Verbose, $"tokenizer read over: {tokens.Count}");
        }

        public Token Peek()
        {
            if (tokens.Count <= idx) return null;
            return tokens[idx];
        }

        public Token Read()
        {
            if (tokens.Count <= idx) return null;
            return tokens[idx++];
        }

        public bool ReadKeyWords(string keyWords)
        {
            var token = Read();
            if (token == null) return false;
            return token.IsKeyWord(keyWords);
        }
    }
    class TextTokenizer : Tokenizer
    {
        // Plain Keywords() Id String Comment Directive
        private TextReader reader;
        private string fileName;
        private long lineNo;
        public TextTokenizer(string str, string fileName, int lineNo)
        {
            reader = new StringReader(str);
            this.fileName = fileName;
            this.lineNo = lineNo;
        }
        private Token current = null;
        private Token last = null;
        private static HashSet<char> singleKeyWords = new HashSet<char>()
        {
            '(', ')' // # /**/
        };
        private bool newLine = true;
        public bool RecHash = false;
        private void Forward()
        {
            current = null;
            if (reader.Peek() == -1) return;
            char first = (char)reader.Read();
            if (char.IsWhiteSpace(first))
            {
                current = new Token { Type = TokenType.Plain, Str = char.ToString(first) };
                if (first == '\n') newLine = true;
                return;
            }
            if (RecHash && newLine && first == '#')
            {
                string result = "";
                while (true)
                {
                    int next = reader.Read();
                    if (next == -1) break;
                    char nchr = (char)next;
                    result += nchr;
                    if (nchr == '\n') break;
                }
                string trimTest = result.TrimStart();
                if (trimTest.Length > 0 && char.IsDigit(trimTest[0]))
                {
                    current = new Token { Type = TokenType.Plain, Str = "#" + result };
                    return;
                }
                current = new Token { Type = TokenType.Directive, Str = "#" + result, Directive = new Directive(result) };
                return;
            }
            newLine = false;
            if (singleKeyWords.Contains(first))
            {
                if (first == '(' && last != null && last.Type == TokenType.ID)
                {
                    last.IsCall = true;
                }
                current = new Token { Type = TokenType.Keywords, Str = char.ToString(first) };
                return;
            }
            if (first == '/')
            {
                int next = reader.Peek();
                if (next !=-1)
                {
                    char nchr = (char)next;
                    if(nchr == '/')
                    {
                        reader.Read();
                        string result = "//";
                        while (true)
                        {
                            next = reader.Read();
                            if (next == -1) break;
                            nchr = (char)next;
                            result += nchr;
                            if (nchr == '\n') break;
                        }
                        current = new Token { Type = TokenType.Comment, Str = new string('\n', result.Count(x => x == '\n')) };
                        return;
                    }
                    else if (nchr == '*')
                    {
                        reader.Read();
                        string result = "/*";
                        bool isStar = false;
                        while (true)
                        {
                            next = reader.Read();
                            if (next == -1)
                            {
                                throw new PreprocessorException("comment not finish");
                            }
                            nchr = (char)next;
                            result += nchr;
                            if (isStar && nchr == '/')
                            {
                                break;
                            }
                            isStar = nchr == '*';
                        }
                        current = new Token { Type = TokenType.Comment, Str = new string('\n', result.Count(x => x == '\n')) };
                        return;
                    }
                }
            }
            if (IdReader.IsIdStart(first))
            {
                string ret = char.ToString(first);
                while (true)
                {
                    int result = reader.Peek();
                    if (result == -1) break;
                    if (!IdReader.IsIdBody((char)result)) break;
                    ret += (char)reader.Read();
                }
                current = new Token { Type = TokenType.ID, Str = ret };
                return;
            }
            if ('"' == first || '\'' == first)
            {
                bool isRe = false;
                string ret = char.ToString(first);
                while (true)
                {
                    int next = reader.Read();
                    if (next == -1) throw new PreprocessorException("string not finish");
                    char chr = (char)next;
                    ret += chr;
                    if (!isRe && chr == first) break;
                    isRe = chr == '\\';
                }
                current = new Token()
                {
                    Type = TokenType.String,
                    Str = ret
                };
                return;
            }
            current = new Token()
            {
                Type = TokenType.Plain,
                Str = char.ToString(first)
            };
        }
        public Token Peek()
        {
            if (current == null)
            {
                try
                {
                    Forward();
                    if (current != null && !current.IsEmpty()) last = current;
                }
                catch(PreprocessorException err)
                {
                    throw new PreprocessorException($"{fileName}:{lineNo}: text tokenizer: {err.Message}");
                }
                if (current != null)
                {
                    current.FileName = fileName;
                    current.LineNo = lineNo;
                    current.Lines = current.Str.LongCount(x => x == '\n');
                }
            }
            return current;
        }
        public bool ReadKeyWords(string keyWords)
        {
            var token = Read();
            if (token == null) return false;
            return token.IsKeyWord(keyWords);
        }
        public Token Read()
        {
            Token ret = Peek();
            lineNo += ret.Lines;
            current = null;
            return ret;
        }
    }
    class MacroRuleTokenizer : Tokenizer
    {
        // Plain Keywords() Id String Directive
        private TextReader reader;
        private string fileName;
        private long lineNo;
        public MacroRuleTokenizer(Token token, string re)
        {
            reader = new StringReader(re);
            fileName = token.FileName;
            lineNo = token.LineNo;
        }
        private Token current = null;
        private Token last = null;
        private static HashSet<char> singleKeyWords = new HashSet<char>()
        {
            '(', ')' // # ## #@
        };
        private void Forward()
        {
            current = null;
            if (reader.Peek() == -1) return;
            char first = (char)reader.Read();
            if (first == '#')
            {
                int next = reader.Peek();
                if ("@#".Contains((char)next))
                {
                    reader.Read();
                    current = new Token { Type = TokenType.Keywords, Str = "#" + (char)next };
                    return;
                }
                current = new Token { Type = TokenType.Keywords, Str = "#" };
                return;
            }
            if (singleKeyWords.Contains(first))
            {
                if (first == '(' && last != null && last.Type == TokenType.ID)
                {
                    last.IsCall = true;
                }
                current = new Token { Type = TokenType.Keywords, Str = char.ToString(first) };
                return;
            }
            if (IdReader.IsIdStart(first))
            {
                string ret = char.ToString(first);
                while (true)
                {
                    int result = reader.Peek();
                    if (result == -1) break;
                    if (!IdReader.IsIdBody((char)result)) break;
                    ret += (char)reader.Read();
                }
                current = new Token { Type = TokenType.ID, Str = ret };
                return;
            }
            if ('"' == first || '\'' == first)
            {
                bool isRe = false;
                string ret = char.ToString(first);
                while (true)
                {
                    int next = reader.Read();
                    if (next == -1) throw new PreprocessorException("string not finish");
                    char chr = (char)next;
                    ret += chr;
                    if (!isRe && chr == first) break;
                    isRe = chr == '\\';
                }
                current = new Token()
                {
                    Type = TokenType.String,
                    Str = ret
                };
                return;
            }
            current = new Token()
            {
                Type = TokenType.Plain,
                Str = char.ToString(first)
            };
        }
        public Token Peek()
        {
            if (current == null)
            {
                try
                {
                    Forward();
                    if (current != null && !current.IsEmpty()) last = current;
                }
                catch (PreprocessorException err)
                {
                    throw new PreprocessorException($"{fileName}:{lineNo}: marco args tokenizer: {err.Message}");
                }
                if (current != null)
                {
                    current.FileName = fileName;
                    current.LineNo = lineNo;
                }
            }
            return current;
        }
        public bool ReadKeyWords(string keyWords)
        {
            var token = Read();
            if (token == null) return false;
            return token.IsKeyWord(keyWords);
        }
        public Token Read()
        {
            Token ret = Peek();
            lineNo += ret.Lines;
            current = null;
            return ret;
        }
    }
    class MacroRuleParse
    {
        private Tokenizer tokenizer;

        public MacroRuleParse(Tokenizer tokenizer)
        {
            this.tokenizer = tokenizer;
        }

        public List<Token> Parse()
        {
            var ret = new List<Token>();
            while(tokenizer.Peek() != null)
            {
                Token tk = tokenizer.Read();
                if (tk.IsKeyWord("#") || tk.IsKeyWord("#@") || tk.IsKeyWord("##"))
                {
                    Token id;
                    while (true)
                    {
                        Token tmp = tokenizer.Read();
                        if (tmp == null)
                        {
                            throw new PreprocessorException($"no following ID for {tk.Str}");
                        }
                        if (tmp.IsEmpty()) continue;
                        if (tmp.Type == TokenType.ID)
                        {
                            id = tmp;
                            break;
                        }
                        throw new PreprocessorException($"the following after {tk.Str} is not ID");
                    }
                    if (tk.IsKeyWord("##"))
                    {
                        Token pre;
                        while (true)
                        {
                            if (ret.Count == 0)
                            {
                                throw new PreprocessorException($"no prefix ID for {tk.Str}");
                            }
                            if (ret.Last().IsEmpty())
                            {
                                ret.RemoveAt(ret.Count - 1);
                                continue;
                            }
                            if (ret.Last().Type == TokenType.ID)
                            {
                                pre = ret.Last();
                                break;
                            }
                            throw new PreprocessorException($"the prefix after {tk.Str} is not ID");
                        }
                        if (pre.Combination == null) pre.Combination = new List<string>();
                        pre.Combination.Add(id.Str);
                        continue;
                    }
                    else
                    {
                        tk.Arg = id.Str;
                    }
                }
                ret.Add(tk);
            }
            return ret;
        }
    }
    class CondTokenizer : Tokenizer
    {
        private TextReader reader;
        public CondTokenizer(string argStr)
        {
            reader = new StringReader(argStr);
        }
        private Token current = null;
        private static HashSet<char> singleKeyWords = new HashSet<char>()
        {
            '%', '(', ')', '+', ',', '-', '^', '~', '"', '\'', '/', ':', '?'
        };
        public static Dictionary<char, string> multiKeyWords = new Dictionary<char, string>()
        {
            { '/', "/" },
            { '!', "=" },
            { '&', "&" },
            { '<', "<=" },
            { '>', ">=" },
            { '|', "|" },
            { '*', "*" },
            { '=', "=" }
        };
        private void Forward()
        {
            current = null;
            while (true)
            {
                int result = reader.Peek();
                if (result == -1) return;
                if (!char.IsWhiteSpace((char)result)) break;
                reader.Read();
            }
            char first = (char)reader.Read();
            if (singleKeyWords.Contains(first))
            {
                current = new Token { Type = TokenType.Keywords, Str = char.ToString(first) };
                return;
            }
            if (multiKeyWords.ContainsKey(first))
            {
                int next = reader.Peek();
                if (next == -1 || !multiKeyWords[first].Contains((char)next))
                {
                    current = new Token { Type = TokenType.Keywords, Str = char.ToString(first) };
                }
                else
                {
                    reader.Read();
                    current = new Token { Type = TokenType.Keywords, Str = new string(new char[] { first, (char)next }) };
                }
                return;
            }
            if (IdReader.IsIdStart(first))
            {
                string ret = char.ToString(first);
                while (true)
                {
                    int result = reader.Peek();
                    if (result == -1) break;
                    if (!IdReader.IsIdBody((char)result)) break;
                    ret += (char)reader.Read();
                }
                current = new Token { Type = TokenType.ID, Str = ret };
                return;
            }
            if (char.IsDigit(first))
            {
                string ret = char.ToString(first);
                bool hasDot = false;
                while (true)
                {
                    int result = reader.Peek();
                    char chr = (char)result;
                    if (result == -1) break;
                    if (chr == '.')
                    {
                        if (hasDot) break;
                        hasDot = true;
                    }
                    else if (chr == '-' && hasDot && char.ToLower(ret.Last()) == 'e')
                    {
                        // by pass 1.0e[-]3
                    }
                    else if (!char.IsLetterOrDigit(chr)) break;
                    ret += (char)reader.Read();
                }
                if (hasDot)
                {
                    throw new PreprocessorException($"float is not allowed in cond expression");
                }
                string origin = ret;
                ret = ret.ToLower();
                int @base = 10;
                bool unsigned = false;
                if (ret.EndsWith("u"))
                {
                    unsigned = true;
                    ret = ret[0..^1];
                }
                else if (ret.EndsWith("sz"))
                {
                    unsigned = true;
                    ret = ret[0..^2];
                }

                while (ret.Length > 0 && char.IsLetter(ret[ret.Length - 1])) ret = ret[0..^1];
                if (ret.StartsWith("0x"))
                {
                    @base = 16;
                    ret = ret[2..];
                }
                else if (ret.StartsWith("0b"))
                {
                    @base = 2;
                    ret = ret[2..];
                }
                else if (ret.StartsWith("0") && ret != "0")
                {
                    @base = 8;
                    ret = ret[1..];
                }
                dynamic val = unsigned ? Convert.ToUInt64(ret, @base) : Convert.ToInt64(ret, @base);
                current = new Token
                {
                    Type = TokenType.Int,
                    Str = ret,
                    Parsed = val
                };
                return;
            }
            if ('"' == first || '\'' == first)
            {
                bool isRe = false;
                string ret = char.ToString(first);
                while (true)
                {
                    int next = reader.Read();
                    if (next == -1) throw new PreprocessorException("char or string not end");
                    char chr = (char)next;
                    ret += chr;
                    if (!isRe && chr == first) break;
                    isRe = chr == '\\';
                }
                if ('"' == first)
                {
                    throw new PreprocessorException("string not allowed");
                }
                try
                {
                    current = new Token()
                    {
                        Type = TokenType.String,
                        Str = ret,
                        Parsed = (long)(Regex.Unescape(ret[1..^1])[0])
                    };
                }
                catch (Exception err)
                {
                    throw new PreprocessorException($"char({ret}) unescape failed: {err.Message}");
                }
                Program.Show(ShowLevel.Verbose, $"parsed char from {ret} is {current.Parsed}");
                return;
            }
            throw new PreprocessorException("token cannot be parsed");
        }
        public Token Peek()
        {
            if (current == null) Forward();
            return current;
        }
        public bool ReadKeyWords(string keyWords)
        {
            var token = Read();
            if (token == null) return false;
            return token.IsKeyWord(keyWords);
        }
        public Token Read()
        {
            Token ret = Peek();
            current = null;
            return ret;
        }
    }
    class CondParser
    {
        private CondTokenizer reader;
        public CondParser(string argStr)
        {
            reader = new CondTokenizer(argStr);
        }
        private static long cmpWrapper(dynamic left, dynamic right, Func<dynamic, dynamic, bool> cmp)
        {
            if (left is ulong || right is ulong)
            {
                return cmp((ulong)left, (ulong)right) ? 1 : 0;
            }
            return cmp(left, right) ? 1 : 0;
        }
        private static bool toBool(dynamic val)
        {
            if (val is ulong ul) return ul != 0;
            if (val is long l) return l != 0;
            return (bool)val;
        }
        private delegate ExecutionFunc BinaryExecutionGen(ExecutionFunc left, ExecutionFunc right);
        private List<Dictionary<string, BinaryExecutionGen>> binaryOps = new List<Dictionary<string, BinaryExecutionGen>>()
        {
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "**", (left, right) => (state, _) => Math.Pow(left(state), right(state)) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "*", (left, right) => (state, _) => left(state) * right(state) },
                { "/", (left, right) => (state, _) => left(state) / right(state) },
                { "%", (left, right) => (state, _) => left(state) % right(state) },
                { "//", (left, right) => (state, _) => Math.Floor(left(state) / right(state)) },
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "+", (left, right) => (state, _) => left(state) + right(state) },
                { "-", (left, right) => (state, _) => left(state) - right(state) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "<<", (left, right) => (state, _) => left(state) << right(state) },
                { ">>", (left, right) => (state, _) => left(state) >> right(state) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "<", (left, right) => (state, _) => cmpWrapper(left(state), right(state), new Func<dynamic, dynamic, bool>((x, y) => x < y)) },
                { "<=", (left, right) => (state, _) => cmpWrapper(left(state), right(state), new Func<dynamic, dynamic, bool>((x, y) => x <= y)) },
                { ">", (left, right) => (state, _) => cmpWrapper(left(state), right(state), new Func<dynamic, dynamic, bool>((x, y) => x > y)) },
                { ">=", (left, right) => (state, _) => cmpWrapper(left(state), right(state), new Func<dynamic, dynamic, bool>((x, y) => x >= y)) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "==", (left, right) => (state, _) => cmpWrapper(left(state), right(state), new Func<dynamic, dynamic, bool>((x, y) => x == y)) },
                { "!=", (left, right) => (state, _) => cmpWrapper(left(state), right(state), new Func<dynamic, dynamic, bool>((x, y) => x != y)) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "&", (left, right) => (state, _) => left(state) & right(state) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "^", (left, right) => (state, _) => left(state) ^ right(state) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "|", (left, right) => (state, _) => left(state) | right(state) }
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
#pragma warning disable CS0078 // "l" 后缀容易与数字 "1" 混淆
                { "&&", (left, right) => (state, _) => toBool(left(state)) && toBool(right(state))  ? 1l : 0l }
#pragma warning restore CS0078 // "l" 后缀容易与数字 "1" 混淆
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
#pragma warning disable CS0078 // "l" 后缀容易与数字 "1" 混淆
                { "||", (left, right) => (state, _) => toBool(left(state)) || toBool(right(state))  ? 1l : 0l }
#pragma warning restore CS0078 // "l" 后缀容易与数字 "1" 混淆
            },
            new Dictionary<string, BinaryExecutionGen>()
            {
                { "?", (left, right) => (state, _) => new KeyValuePair<ExecutionFunc, ExecutionFunc>(left, right) },
                { ":", (left, right) => (state, _) =>
                    {
                        var rr = left(state);
                        return toBool(rr.Key(state)) ? rr.Value(state) : right(state);
                    }
                }
            }
        };
        private delegate ExecutionFunc UnaryExecutionGen(ExecutionFunc value);
        private Dictionary<string, UnaryExecutionGen> unaryOps = new Dictionary<string, UnaryExecutionGen>()
        {
            { "+", value => (state, _) => +value(state) },
            { "-", value => (state, _) => -value(state) },
#pragma warning disable CS0078 // "l" 后缀容易与数字 "1" 混淆
            { "!", value => (state, _) => toBool(value(state)) ? 0l : 1l },
#pragma warning restore CS0078 // "l" 后缀容易与数字 "1" 混淆
            { "~", value => (state, _) => ~value(state) }
        };
        private ExecutionFunc ParseBase()
        {
            var next = reader.Read();
            if (next == null) throw new PreprocessorException("should have something but nothing to parse");
            ExecutionFunc left = null;
            if (next.Type == TokenType.Int || next.Type == TokenType.Float || next.Type == TokenType.String)
            {
                var val = next.Parsed;
                left = (state, _) => val;
            }
            else if (next.Type == TokenType.ID)
            {
                Program.Show(ShowLevel.Infomation, $"Unsolved Id in cond {next.Str}");
                left = (state, _) => (long)0;
            }
            else
            {
                if (!next.IsKeyWord("(")) throw new PreprocessorException("unknown value started");
                left = ParseArgs();
                if (!reader.ReadKeyWords(")")) throw new PreprocessorException("backets not matched");
            }
            return left;
        }
        private ExecutionFunc ParseUnary()
        {
            var next = reader.Peek();
            if (reader.Peek() == null) throw new PreprocessorException("should have something but nothing to parse");
            if (next.Type != TokenType.Keywords || !unaryOps.ContainsKey(next.Str))
            {
                return ParseBase();
            }
            reader.Read();
            return unaryOps[next.Str](ParseUnary());
        }
        private ExecutionFunc ParseBinary(int dep)
        {
            if (dep < 0) return ParseUnary();
            if (reader.Peek() == null) throw new PreprocessorException("should have something but nothing to parse");
            var left = ParseBinary(dep - 1);
            while (true)
            {
                var next = reader.Peek();
                if (next == null) break;
                if (next.Type != TokenType.Keywords || !binaryOps[dep].ContainsKey(next.Str)) break;
                reader.Read();
                var right = ParseBinary(dep - 1);
                left = binaryOps[dep][next.Str](left, right);
            }
            return left;
        }
        private ExecutionFunc ParseBinaryRight()
        {
            if (reader.Peek() == null) throw new PreprocessorException("should have something but nothing to parse");
            var left = ParseBinary(binaryOps.Count - 1);
            return left;
        }
        public ExecutionFunc ParseArgs()
        {
            var funcs = new List<ExecutionFunc>();
            while (true)
            {
                var next = reader.Peek();
                if (next == null || next.IsKeyWord(")")) break;
                funcs.Add(ParseBinaryRight());
                next = reader.Peek();
                if (next == null) break;
                if (!next.IsKeyWord(","))
                {
                    break;
                }
                reader.Read();
            }
            if (funcs.Count == 0) throw new PreprocessorException("cond-root or () should have values");
            return (state, _) =>
            {
                var ret = new List<dynamic>();
                foreach (var func in funcs)
                {
                    dynamic cur = func(state);
                    ret.Add(cur);
                }
                return ret.Last();
            };
        }
        public ExecutionFunc Parse()
        {
            return ParseArgs();
        }
    }
}
