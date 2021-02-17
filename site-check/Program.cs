using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Newtonsoft.Json;

namespace site_check
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine(JsonConvert.SerializeObject(await RunArgs(args)));
        }
        static async Task<Dictionary<string, object>> RunArgs(params string[] args)
        {
            Dictionary<string, object> results = new Dictionary<string, object>();
            try
            {
                foreach (call k in Argh.ToSkript(args).calls) {
                    var env = obj.TopLevel();
                    await env.eval(k);
                    foreach(var kv in env.Output) {
                        results[kv.Key] = kv.Value;
                    }
                }
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.Message);
                return null;
            }
            return results;
        }
    }

    public class Curse: Exception {
        public Curse(string message): base(message) { }
        public static Curse ValueIsNotCall(string arg)
        {
            return new Curse($"Argument {arg} is value, not a call. \nTop level arguments should be calls (start with \"-\"), such as -site");
        }
    }
    public static class Argh {
        public static skript ToSkript (string[] args) {
            if (args.Length == 0) {
                return new skript() { };
            }

            bool isCallName(string maybeCallName) => maybeCallName.StartsWith("-");
            string cleanName(string callName) => new string(callName.SkipWhile(c => c == '-').ToArray());
            int callDepth(string callName) => callName.TakeWhile(c => c == '-').Count();

            bool parsing = true;

            //List<call> stack = new List<call>();
            Stack<call> stack = new Stack<call>();
            stack.Push(new call() { 
                name = "Toplevel",
                depth = 0,
            });
            int currentDepth = 0;
            int idx = 0;
            while (parsing) {
                string tok = args[idx];
                int tokDepth = callDepth(tok);
                bool isCall = isCallName(tok);

                /*
                toplevel
                -site abc
                -site bce
                 */
                if (isCall && tokDepth == currentDepth) {
                    stack.Pop();
                    call k = new call()
                    {
                        name = cleanName(tok),
                        depth = callDepth(tok)
                    };
                    // Add to parent
                    stack.Peek().AddCall(k);
                    // Put on stack for followup calls
                    stack.Push(k);
                } else if (isCall && tokDepth == currentDepth + 1) {
                    call k = new call() {
                        name = cleanName(tok),
                        depth = callDepth(tok),
                    };
                    stack.Peek().AddCall(k);
                    stack.Push(k);
                    currentDepth++;
                } else if (isCallName(tok) && callDepth(tok) < currentDepth && currentDepth > 0) {
                    while(tokDepth < currentDepth) {
                        stack.Pop();
                        currentDepth--;
                    }
                    stack.Pop();
                    call k = new call()
                    {
                        name = cleanName(tok),
                        depth = callDepth(tok)
                    };
                    // Add to parent
                    stack.Peek().AddCall(k);
                    // Put on stack for followup calls
                    stack.Push(k);
                } else if (!isCall && currentDepth > 0) {
                    // Values aren't valid at the top level
                    stack.Peek().AddArg(tok);
                } else if (!isCall) {
                    Curse.ValueIsNotCall(tok);
                }
                idx++;
                parsing = idx < args.Length;
            }

            while(stack.Count > 1) {
                stack.Pop();
            }

            return stack.Peek().block;
        }
    }

    public static class Ensure {
        public static call ArityMatches(this call kall, int expected) { 
            if (kall.args == null && expected == 0) {
                return kall;
            }
            if (kall.args?.Count != expected) {
                throw ObjException.ArityMismatch(kall, 1);
            }
            return kall;
        }
        public static call DomBuilt(this call kall, IDocument dom) {
            if (dom == null)
            {
                throw ObjException.MissingDomAndCalled(kall);
            }
            return kall;
        }
        public static call ElementSelected(this call kall, IElement el) {
            if (el == null)
            {
                throw ObjException.MissingElementAndCalled(kall);
            }
            return kall;
        }
        public static call NoBlock(this call kall)
        {
            if (kall.block != null && kall.block.calls.Count > 0)
            {
                throw ObjException.BlockNotAccepted(kall);
            }
            return kall;
        }

        public static call HasBlock(this call kall)
        {
            if (kall.block != null && kall.block.calls.Count > 0)
            {
                return kall;
            }
            throw ObjException.BlockRequired(kall);
        }
    } 
    public class ObjException: Exception {
        public ObjException(string message): base(message) { }
        public static ObjException ArityMismatch(call kall, int expected) {
            return new ObjException($"Arg mismatch: {kall.DebugName} exepects {expected} argument(s), not {kall.args.Count} arguments");
        }
        public static ObjException MissingDomAndCalled(call kall) {
            return new ObjException($"Order issue: -site should be called before {kall.DebugName}");
        }
        public static ObjException MissingElementAndCalled(call kall) {
            return new ObjException($"Order issue: -site and --id or --sel should be called before {kall.DebugName}");
        }

        public static ObjException ExpressionDiscarded(call current, call prev) {
            return new ObjException($"Calling {current.DebugName} discarded result from {prev.DebugName}. Either use any/all with {prev.DebugName} as a sub-call");
        }

        internal static Exception BlockNotAccepted(call kall)
        {
            return new ObjException($"{kall.name} doesn't accept block. Perhaps you meant to use less dashes for {kall.block.calls[0].name}?");
        }
        internal static Exception BlockRequired(call kall)
        {
            return new ObjException($"{kall.name} requires a block. Perhaps you wanted to use more dashes for the next argument?");
        }

        internal static Exception UrlFailed(call kall)
        {
            return new ObjException($"The URL {kall.args[0]} failed to load!");
        }
    }

    // These classes imitate how Ruby objects and scoping work
    public class obj {
        public Dictionary<string, Func<call, Task<bool>>> funcs = new Dictionary<string, Func<call, Task<bool>>>();
        private Dictionary<string, object> values = new Dictionary<string, object>();
        private bool resultSet = false;
        private object result = null;
        private List<call> trace = new List<call>();
        public Dictionary<string, object> Output { get; set; } = new Dictionary<string, object>();

        public obj() { }
        public async Task eval(call kall)
        {
            bool hadResult = resultSet;
            bool addedResult = await funcs[kall.name](kall);
            if (hadResult && addedResult)
            {
                throw ObjException.ExpressionDiscarded(kall, trace.FindLast(x => true));
            }
            hadResult = addedResult;
            trace.Add(kall);
        }
        public static obj TopLevel() {
            var topLevel = new obj();

            BrowsingContext ctx = new BrowsingContext(Configuration.Default.WithDefaultLoader());

            IDocument dom = null;
            IElement el = null;

            topLevel.funcs["true"] = async (kall) => {
                Ensure.ArityMatches(kall, 0).NoBlock();
                topLevel.result = true;
                return true;
            };
            topLevel.funcs["false"] = async (kall) => {
                Ensure.ArityMatches(kall, 0).NoBlock();
                topLevel.result = false;
                return true;
            };
            topLevel.funcs["out"] = async (kall) => {
                Ensure.ArityMatches(kall, 1);
                topLevel.Output[kall.args[0]] = topLevel.result;
                topLevel.result = null;
                return false;
            };
            topLevel.funcs["all"] = async (kall) => {
                Ensure.HasBlock(kall);
                foreach (call k in kall.block.calls) {
                    if (topLevel.resultSet && topLevel.result is false) {
                        topLevel.result = false;
                        return false;
                    }
                }
                // If we didn't find any results set to false, it then set result to true
                topLevel.result = true;
                return false;
            };

            topLevel.funcs["any"] = async (kall) => {
                Ensure.HasBlock(kall);
                foreach (call k in kall.block.calls) {
                    await topLevel.eval(k);
                    if (topLevel.resultSet && topLevel.result is true) {
                        return false;
                    }
                }
                // If we didn't find any truthy values, then 
                topLevel.result = false;
                return false;
            };

            topLevel.funcs["has-text"] = async (kall) =>
            {
                Ensure.ArityMatches(kall, 1).NoBlock().ElementSelected(el);
                topLevel.result = el.TextContent.Contains(kall.args[0]);
                return true;
            };

            topLevel.funcs["sel"] = async (kall) => {
                Ensure.ArityMatches(kall, 1).NoBlock().DomBuilt(dom);
                el = dom.QuerySelector(kall.args[0]);
                return false;
            };

            topLevel.funcs["id"] = async (kall) => {
                Ensure.ArityMatches(kall, 1).DomBuilt(dom).NoBlock();
                el = dom.GetElementById(kall.args[0]);
                return false;
            };
            topLevel.funcs["site"] = async (kall) => {
                Ensure.ArityMatches(kall, 1);
                dom = await ctx.OpenAsync(kall.args[0]);
                if (kall.block != null)
                {
                    await kall.block.eval(topLevel);
                }
                if (dom == null)
                {
                    ObjException.UrlFailed(kall);
                }
                return false;
            };

            return topLevel;
        }
    }

    public class call {
        // How deep this call is in the nesting. Used for eror handling
        public int depth { get; set; }
        public string name { get; set; }
        public List<string> args { get; set; }
        public skript block { get; set; } 
        public void AddArg(string arg) {
            args = args ?? new List<string>();
            args.Add(arg);
        }
        public void AddCall (call child) {
            block = block ?? new skript();
            block.calls.Add(child);
        }
        public string DebugName { get => new string('-', depth) + name; }
        public bool HasBlock { get => block != null && block.calls.Count > 0; }
    }

    public class skript {
        private static int cnt = 0;
        public List<call> calls { get; set; } = new List<call>();
        public async Task eval (obj env)
        {
            bool isFaulted = false;
            for(var i = 0; i < calls.Count; i++)
            {
                var k = calls[i];
                if (isFaulted && k.name == "out") {
                    env.Output[k.args[0]] = false;
                    return;
                }
                try
                {
                    await env.eval(k);
                } catch (ObjException ex) {
                    env.Output[$"fault{cnt++}"] = ex.Message;
                    isFaulted = true;
                }
            }
        }
    }
}
