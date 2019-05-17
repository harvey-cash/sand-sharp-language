﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Command {

    // Run all commands in array of command strings
    public static (Dictionary<string, object>, object) Run(Dictionary<string, object> memory, string[] commands) {
        try {
            object result = null;
            for (int i = 0; i < commands.Length; i++) {
                (memory, result) = Run(memory, commands[i]);
            }
            return (memory, result);
        } catch (Exception e) {
            Debug.LogError(e);
            Terminal.terminal.Print("Script terminated.");
            return (memory, null);
        }
    }

    // Run subscript on copy of memory, and remove changes to anything that wasn't defined in the outer scope
    public static (Dictionary<string, object>, object) RunSubscript(Dictionary<string, object> memory, string subscript) {
        Dictionary<string, object> subMemory = new Dictionary<string, object>(memory);
        return RunSubscript(memory, subMemory, subscript);
    }

    // Run subscript on subMemory, and remove changes to anything that wasn't defined in the outer scope's memory
    public static (Dictionary<string, object>, object) RunSubscript(Dictionary<string, object> memory, Dictionary<string, object> subMemory, string subscript) {
        string[] subCommands = ScriptParser.ParseCommandStrings(subscript);

        // We ignore whatever the runtime equates to, 
        // and instead specifically look for the value of the "return" variable
        (subMemory, _) = Run(subMemory, subCommands);
        object result = ParseReturn(subMemory);
        return (ResolveSubScope(memory, subMemory), result);
    }

    // Look in the given memory for the value of "return"
    public static object ParseReturn(Dictionary<string, object> memory) {
        bool defined = memory.TryGetValue("return", out object value);
        if (defined) {
            return value;
        }
        else {
            return null;
        }
    }

    // Modify what already existed, forget all else
    public static Dictionary<string, object> ResolveSubScope(Dictionary<string, object> memory, Dictionary<string, object> subMemory) {
        Dictionary<string, object> modifiedOuterScope = new Dictionary<string, object>();

        // Avoid modifying memory while enumerating over it
        foreach (var entry in memory) {
            modifiedOuterScope[entry.Key] = subMemory[entry.Key];
        }
        foreach (var entry in modifiedOuterScope) {
            memory[entry.Key] = modifiedOuterScope[entry.Key];
        }
        return memory;
    }

    // Run a command string
    public static (Dictionary<string, object>, object) Run(Dictionary<string, object> memory, string command) {

        // Base case, evaluates to literal
        if (ScriptParser.IsNumber(command)) {
            return (memory, float.Parse(command));
        }
        if (ScriptParser.IsStringLiteral(command)) {
            return (memory, command.Substring(1, command.Length - 2));
        }
        // Operator statement
        if (ScriptParser.IsOperationStatement(command, 
            out string left, out string opstr, out string right)) {
            
            ScriptParser.IsOperator(opstr, out bool isBool);
            object leftObj, rightObj;
            (memory, leftObj) = Run(memory, left);
            (memory, rightObj) = Run(memory, right);

            float leftEval = (float)leftObj;
            float rightEval = (float)rightObj;

            if (isBool) {
                ScriptParser.BoolOperator op = ScriptParser.BoolOp(opstr);
                return (memory, op(leftEval, rightEval));
            }
            else {
                ScriptParser.FloatOperator op = ScriptParser.FloatOp(opstr);
                return (memory, op(leftEval, rightEval));
            }
        }
        // Name of something in memory, evaluate and return it
        if (memory.ContainsKey(command)) {
            return Run(memory, memory[command].ToString());
        }

        // Else, look for statement or method of some sort

        string buffer = "";
        for (int i = 0; i < command.Length; i++) {
            char c = command[i];

            // Value assignment
            if (c == '=') {
                (memory, memory[buffer]) = Run(memory, command.Substring(i + 1));
                return (memory, memory[buffer]);
            }

            // Else neither an assignment nor a simple statement
            // The start of a method?
            if (c == '(') {
                string methodName = buffer;
                // include open bracket for parsing parameters
                object[] parameters = ParseParameters(command.Substring(i), memory);
                string subscript = ParseSubscript(command.Substring(i + 1));

                return LookupAndRun(memory, methodName, parameters, subscript);
            }

            // Must just be some other letter or number!
            // Continue to add to the buffer
            buffer += c;
        }

        // We reached the end without calling anything interesting? Oh.
        // I guess we don't like that. Probably doesn't exist
        Terminal.terminal.Print("\"" + buffer + "\" is undefined.");
        throw new Exception();
    }

    // Look for a method to run. Error if it doesn't exist!
    private static (Dictionary<string, object>, object) LookupAndRun(
        Dictionary<string, object> memory, string name, object[] parameters, string subscript) {

        // Built-in method?
        bool provided = Library.methods.TryGetValue(name, out Library.Method Method);        
        if (provided) {
            //Debug.Log(subscript);
            return Method(memory, name, parameters, subscript);
        }

        // User-defined method?
        bool defined = memory.TryGetValue(name, out object method);
        if (defined) {
            return UserMethod.CallUserMethod(memory, (UserMethod)method, parameters);
        }

        // Else:
        Terminal.terminal.Print("\"" + name + "\" is undefined.");
        return (memory, null);
    }


    private static string ParseSubscript(string restOfCommand) {
        // record inside brace until close brace of same level
        string buffer = "";
        int depth = 0;

        for (int i = 0; i < restOfCommand.Length; i++) {
            char c = restOfCommand[i];

            if (c == '{') {                
                if (depth == 0) { depth++; continue; } // Ignore first open curly brace
                else { depth++; }
            }
            if (c == '}') {
                depth--;
                if (depth == 0) { return buffer; } // Last curly close brace, return
            }

            // If within substring, add to buffer
            if (depth > 0) { buffer += c; }
        }
        return null;
    }

    // Don't split on ','s within brackets! (Methods as parameters...)
    private static object[] ParseParameters(string restOfCommand, Dictionary<string,object> memory) {        
        string[] paramStrings = ScriptParser.SplitParameters(restOfCommand);
        object[] parameters = new object[paramStrings.Length];
        for (int p = 0; p < parameters.Length; p++) {
            (memory, parameters[p]) = Run(memory, paramStrings[p]);
        }        

        return parameters;
    }
}
