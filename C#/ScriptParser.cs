﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class ScriptParser {

    public static char[] ignoreChars = new char[] { '\t' };

    // We don't like spaces anywhere other than within string literals
    public static string[] ParseCommandStrings(string scriptString) {
        List<string> listCommands = new List<string>();

        int depthSubscript = 0;
        bool withinSubscript = false;
        bool withinStringLiteral = false;

        string bufferCommand = "";

        for (int i = 0; i < scriptString.Length; i++) {
            char c = scriptString[i];
            // Skip over certain characters
            if (Array.Exists(ignoreChars, x => x == c)) {
                continue;
            }

            /* ~~~~~ WITHIN SUBSCRIPT ~~~~~ */

            if (withinSubscript) {
                // Increase depth
                if (c == '{') { bufferCommand += c; depthSubscript++; continue; }

                // Decrease depth
                if (c == '}' && withinSubscript) {
                    bufferCommand += c;
                    depthSubscript--;
                    // Broke the surface
                    if (depthSubscript < 1) {
                        listCommands.Add(bufferCommand);
                        bufferCommand = "";
                        withinSubscript = false;
                    }
                    continue;
                }

                // Otherwise, simply add to buffer
                bufferCommand += c;
                continue;
            }

            /* ~~~~~ NOT WITHIN SUBSCRIPT ~~~~~ */

            if (c == '"') {
                withinStringLiteral = !withinStringLiteral;
            }

            /* ~~~~~ WITHIN STRING (NOT SUBSCRIPT) ~~~~~ */

            if (withinStringLiteral) {
                bufferCommand += c;
                continue;
            }

            /* ~~~~~ NOT WITHIN STRING (NOR SUBSCRIPT) ~~~~~ */

            // Comments use '#', ignore following until newline
            if (c == '#') {
                if (bufferCommand.Length > 0) { listCommands.Add(bufferCommand); }
                bufferCommand = "";
                do { i++; } while (i < scriptString.Length - 1 && scriptString[i + 1] != '\n');
                continue;
            }

            // Ignore spaces, except to tell us about particular keywords
            if (c == ' ') {
                if (bufferCommand == "def") { bufferCommand += '~'; }
                continue;
            }

            // Begin subscript
            if (c == '{') {
                bufferCommand += c;
                depthSubscript++;
                withinSubscript = true;
                continue;
            }

            // Split on line breaks
            if (c == '\n') {
                if (bufferCommand.Length > 0) {
                    listCommands.Add(bufferCommand);
                    bufferCommand = "";
                }
                continue;
            }

            // Anything else goes in the buffer
            bufferCommand += c;
            continue;
        }

        // Final command
        if (bufferCommand.Length > 0) {
            listCommands.Add(bufferCommand);
        }

        // Reword method declarations before returning
        return RewordDefs(listCommands);
    }
    
    // def is actually a method that gets called with parameters for
    // method name and parameter names
    private static string[] RewordDefs(List<string> listCommands) {

        (int,string) latestIf = (-1, null);
        for (int i = 0; i < listCommands.Count; i++) {

            bool CommandIs(string pattern) {
                return listCommands[i].Length > pattern.Length
                    && listCommands[i].Substring(0, pattern.Length) == pattern;
            }

            if (CommandIs("def~")) { listCommands[i] = ReplaceDef(listCommands[i]); }
            if (CommandIs("for(")) { listCommands[i] = ReplaceFor(listCommands[i]); }
            if (CommandIs("if(")) { latestIf = RememberIf(i, listCommands[i]); }
            if (CommandIs("else{")) { listCommands[i] = ReplaceElse(i, latestIf, listCommands[i]); }    
        }

        return listCommands.ToArray();
    }

    // Replace def keyword command with a call to "def" method
    private static string ReplaceDef(string command) {
        string[] parameters = SplitParameters(command);

        string name = null;

        int paramEndIndex = 0;
        for (int j = 4; j < command.Length; j++) {
            char c = command[j];

            if (name == null && c == '(') { name = command.Substring(4, j - 4); }
            if (c == '{') { paramEndIndex = j; break; }
        }

        string methodDeclaration = "def(\"" + name + "\"";
        for (int p = 0; p < parameters.Length; p++) {
            methodDeclaration += ",\"" + parameters[p] + "\"";
        }
        methodDeclaration += ")";

        return methodDeclaration + command.Substring(paramEndIndex);
    }

    // Replace for loop syntax with a call to the "for" method
    private static string ReplaceFor(string command) {
        string[] parameters = SplitParameters(command);
        string decl = parameters[0];
        string variable = "";
        int index = 6;
        while (decl[0] != '=') {
            index++;
            variable += decl[0];
            decl = decl.Substring(1);
        }
        string start = decl.Substring(1);

        return "for(\"" + variable + "\"," + start + command.Substring(index);
    }

    // Keep track of last declared if statement
    private static (int, string) RememberIf(int i, string command) {
        string[] parameters = SplitParameters(command);
        (int, string) latestIf = (i, "(");
        for (int p = 0; p < parameters.Length - 1; p++) {
            latestIf.Item2 += p + ",";
        }
        if (parameters.Length > 0) {
            latestIf.Item2 += parameters[parameters.Length - 1] + ")";
        }
        return latestIf;
    }

    // If last command was an 'if', replace this else with !'if'
    private static string ReplaceElse(int i, (int,string) latestIf, string command) {
        if (i != latestIf.Item1 + 1) {
            throw new Exception("Else must follow if!");
        }

        string restOfCommand = command.Substring(4);
        string startOfCommand = "if(!" + latestIf.Item2 + ")";
        return startOfCommand + restOfCommand;
    }

    public static bool IsOperator(string opstr) { return IsOperator(opstr, out bool isBool); }
    public static bool IsOperator(char c) { return IsOperator(c.ToString(), out bool isBool); }
    public static bool IsOperator(char c, out bool isBool) { return IsOperator(c.ToString(), out isBool); }
    public static bool IsOperator(string opstr, out bool isBool) {
        try {
            BoolOp(opstr);
            isBool = true;
            return true;
        }
        catch {
            try {
                FloatOp(opstr);
                isBool = false;
                return true;
            }
            catch {
                isBool = false;
                return false;
            }
        }
    }

    public delegate bool BoolOperator(float a, float b);
    public static BoolOperator BoolOp(char c) { return BoolOp(c.ToString()); }
    public static BoolOperator BoolOp(string opstr) {
        if (opstr.Equals("=="))
            return (a, b) => a == b;
        if (opstr.Equals("!="))
            return (a, b) => a != b;
        if (opstr.Equals(">"))
            return (a, b) => a > b;
        if (opstr.Equals(">="))
            return (a, b) => a >= b;
        if (opstr.Equals("<"))
            return (a, b) => a < b;
        if (opstr.Equals("<="))
            return (a, b) => a <= b;

        throw new Exception();
    }

    public delegate float FloatOperator(float a, float b);
    public static FloatOperator FloatOp(char c) { return FloatOp(c.ToString()); }
    public static FloatOperator FloatOp(string opstr) {
        if (opstr.Equals("+"))
            return (a, b) => a + b;
        if (opstr.Equals("-"))
            return (a, b) => a - b;
        if (opstr.Equals("*"))
            return (a, b) => a * b;
        if (opstr.Equals("/"))
            return (a, b) => a / b;
        if (opstr.Equals("%"))
            return (a, b) => a % b;
        if (opstr.Equals("^"))
            return (a, b) => (float)Math.Pow(a, b);

        throw new Exception();
    }

    public static bool IsAlphaNumeric(char c) { return IsAlphaNumeric(c.ToString()); }
    public static bool IsAlphaNumeric(string word) {
        Regex r = new Regex("^[a-zA-Z0-9]*$");
        return r.IsMatch(word);
    }

    public static bool IsNumber(string word) {
        return float.TryParse(word, out float val);
    }

    public static bool IsStringLiteral(string word) {
        if (word.Length < 1) { return false; }
        return word[0] == '\"';
    }

    // Check whether statement is an assignment
    public static bool IsAssignment(string statement,
        out string name, out string value) {
        name = null;
        value = null;

        for (int i = 0; i < statement.Length; i++) {
            if (statement[i] == '=') {
                if (statement[i + 1] == '(' || IsAlphaNumeric(statement[i + 1])) {
                    name = statement.Substring(0, i);
                    value = statement.Substring(i + 1);
                    return true;
                }
                else {
                    return false;
                }
            }
            else if (!IsAlphaNumeric(statement[i])) {
                return false;
            }
        }
        return false;
    }

    public static bool AllInBrackets(string statement) {
        if (statement.Length < 1) { return false; }

        if (statement[0] != '(' || statement[statement.Length - 1] != ')') {
            return false;
        }
        string stripped = statement.Substring(1, statement.Length - 2);

        int depth = 1;
        bool withinString = false;
        for (int i = 0; i < stripped.Length; i++) {
            char c = stripped[i];

            if (c == '(' || c == '{') { depth++; }
            if (c == ')' || c == '}') { depth--; }
            if (c == '"') { withinString = !withinString; }

            // broke the surface... that's not allowed!
            if (depth < 1) {
                return false;
            }
        }

        return true;
    }

    // Must only be called for valid opstrings
    public static int GetPrecedence(string opstr) {
        if (!IsOperator(opstr, out bool isBool)) { throw new Exception(); }

        // bools have highest precedence
        if (isBool) { return 3; }

        if (opstr.Equals("^")) { return 2; }
        if (opstr.Equals("*") || opstr.Equals("/") || opstr.Equals("%")) { return 1; }

        else {
            return 0;
        }
    }

    private static int GetMaxPrecedence(string[] components) {
        int opCount = Mathf.FloorToInt(components.Length / 2);
        if (opCount == 0) { throw new Exception(); }

        // index, precedence
        (int, int) indexPrecedence = (-1, -1);

        for (int j = 0; j < opCount; j++) {
            int i = (j * 2) + 1;
            string op = components[i];
            int p = GetPrecedence(op);

            if (p > indexPrecedence.Item2) {
                indexPrecedence = (i, p);
            }
        }
        return indexPrecedence.Item1;
    }

    // Return true if valid equation
    // if so, return components
    public static bool ParseEquation(string statement,
        out string left, out string opstr, out string right) {

        try {
            string[] components = GetEquationComponents(statement);

            int index = GetMaxPrecedence(components);

            opstr = components[index];
            left = "";
            for (int i = 0; i < index; i++) {
                left += components[i];
            }
            right = "";
            for (int i = index + 1; i < components.Length; i++) {
                right += components[i];
            }

            return true;

        } catch {
            left = null;
            opstr = null;
            right = null;
            return false;
        }
    }

    // MUST BE: var, (op, var)+
    private static string[] GetEquationComponents(string statement) {
        int depth = 0;
        bool withinString = false;
        List<string> components = new List<string>();
        string buffer = "";

        for (int i = 0; i < statement.Length; i++) {
            char c = statement[i];

            if (c == '(' || c == '{') { depth++; }
            if (c == ')' || c == '}') { depth--; }
            if (c == '"') { withinString = !withinString; }

            // if (depth > 0 || withinString) { continue; }

            if (depth > 0 || withinString) {
                buffer += c;
                continue;
            }

            // this and next form an operator
            if (i < statement.Length - 1 && IsOperator(c.ToString() + statement[i + 1])) {
                components.Add(buffer); //var
                components.Add(c.ToString() + statement[i + 1]); //op
                buffer = "";
                i++; // skip next
                continue;
            }
            // just this forms an operator
            else if (IsOperator(c)) {
                components.Add(buffer);
                components.Add(c.ToString());
                buffer = "";
                continue;
            }
            // regular surface alphanumeric letter
            else {
                buffer += c;
            }
        }
        components.Add(buffer);

        for (int i = 0; i < components.Count; i++) {
            if (components[i].Length < 1) { throw new Exception(); }
        }

        return components.ToArray();
    }

    public static string DictionaryToString<T>(Dictionary<string, T> dict) {
        string buffer = "";
        foreach (KeyValuePair<string, T> kvp in dict) {
            buffer += kvp.ToString() + ",";
        }
        return buffer;
    }
    public static string DictionaryKeys<T>(Dictionary<string, T> dict) {
        string buffer = "";
        foreach (KeyValuePair<string, T> kvp in dict) {
            buffer += kvp.Key + "\n";
        }
        return buffer;
    }

    public static string ParseSubscript(string restOfCommand) {
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

    // Step through parameters from first bracket (which must be there!) until final
    public static string[] SplitParameters(string restOfCommand) {
        while (restOfCommand[0] != '(') {
            restOfCommand = restOfCommand.Substring(1);
        }
        string paramString = restOfCommand.Substring(1);

        List<string> paramsList = new List<string>();
        string paramBuffer = "";
        int depth = 0;
        for (int i = 0; i < paramString.Length; i++) {
            char c = paramString[i];

            if (c == '(') { depth++; }
            if (c == ')') {
                depth--;
                if (depth < 0) { break; } // parameters ended
            }

            // outside of any sub-brackets
            if (c == ',' && depth == 0) {
                paramsList.Add(paramBuffer);
                paramBuffer = "";
                continue;
            }

            paramBuffer += c;
        }
        if (paramBuffer.Length > 0) {
            paramsList.Add(paramBuffer);
        }
        return paramsList.ToArray();
    }

    public static string ArrayToString(object[] array) {
        string buffer = "";
        if (array.Length > 0) {            
            for (int i = 0; i < array.Length - 1; i++) {
                buffer += array[i].ToString() + ", ";
            }
            buffer += array[array.Length-1];
        }
        return buffer;        
    }
}
