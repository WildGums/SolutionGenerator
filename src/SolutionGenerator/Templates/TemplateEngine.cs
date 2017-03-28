﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TemplateEngine.cs" company="WildGums">
//   Copyright (c) 2008 - 2016 WildGums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace SolutionGenerator.Templates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Catel;
    using Catel.Logging;
    using Catel.Reflection;

    public class TemplateEngine
    {
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        private const string TemplateKeyEnd = "]]";
        private const string TemplateBeginForeach = "BeginForeach ";
        private static readonly string TemplateBeginForeachKey = $"[[{TemplateBeginForeach}";
        private const string TemplateEndForeachKey = "[[EndForeach]]";
        private const char ModifierSeparator = '|';

        private readonly TemplateLoader _templateLoader;

        public TemplateEngine(TemplateLoader templateLoader)
        {
            Argument.IsNotNull(() => templateLoader);

            _templateLoader = templateLoader;
        }

        public string ReplaceValues(string templateContent, ITemplate templateModel)
        {
            Argument.IsNotNullOrWhitespace(() => templateContent);
            Argument.IsNotNull(() => templateModel);

            Log.Debug("Filling template with template data");

            var possibleDataPrefixes = templateModel.GetPossibleDataPrefixes();

            var allPossiblePrefixes = new List<string>();
            allPossiblePrefixes.Add(TemplateBeginForeachKey);
            allPossiblePrefixes.AddRange(possibleDataPrefixes.Select(x => $"[[{x}"));

            foreach (var possiblePrefix in allPossiblePrefixes)
            {
                var keyPrefix = possiblePrefix;

                var index = templateContent.IndexOfIgnoreCase(keyPrefix);

                // TODO: Optimize by using regular expressions

                while (index >= 0)
                {
                    // Search for the whole key
                    var keyStart = index + keyPrefix.Length;
                    var keyEnd = templateContent.IndexOfAny(new[] { '|', ']' }, keyStart);
                    if (keyEnd < 0)
                    {
                        throw Log.ErrorAndCreateException<SolutionGeneratorException>($"Can't find end of key prefix '{keyPrefix}' at position '{index}'");
                    }

                    var key = templateContent.Substring(keyStart, keyEnd - keyStart).Trim();

                    Log.Debug($"Found template key '{keyPrefix}{key}' at position '{index}'");

                    // Retrieve modifiers that are located after the key
                    var endPos = templateContent.IndexOf(TemplateKeyEnd, index);
                    if (endPos < 0)
                    {
                        throw Log.ErrorAndCreateException<SolutionGeneratorException>($"Can't find end of key '{key}' at position '{index}'");
                    }

                    endPos += TemplateKeyEnd.Length;

                    var partToReplaceStart = index;
                    var partToReplaceLength = 0;
                    var contentToReplaceWith = string.Empty;

                    if (keyPrefix.EqualsIgnoreCase(TemplateBeginForeachKey))
                    {
                        var collection = (new[] { templateModel }).GetForEachCollection(key);
                        if (collection == null)
                        {
                            // Foreach is not for this template, ignore
                            index = FindNextKeyIndex(templateContent, keyPrefix, index);
                            continue;
                        }

                        // Search for the end
                        var foreachEnd = templateContent.IndexOf(TemplateEndForeachKey, endPos, StringComparison.OrdinalIgnoreCase);
                        if (foreachEnd < 0)
                        {
                            throw Log.ErrorAndCreateException<SolutionGeneratorException>($"Can't find end of foreach key '{key}' at position '{index}'");
                        }

                        var foreachTemplate = templateContent.Substring(endPos, foreachEnd - endPos);

                        foreach (var collectionItem in collection)
                        {
                            contentToReplaceWith = GenerateForeachContent(collectionItem, foreachTemplate);
                        }

                        foreachEnd += TemplateEndForeachKey.Length;
                        partToReplaceLength = foreachEnd - index;
                    }

                    // TODO: Add other special cases here if necessary

                    else
                    {
                        // Regular replacement
                        if (!allPossiblePrefixes.Any(x => x.EqualsIgnoreCase(keyPrefix)))
                        {
                            // Not a value for this template
                            index = FindNextKeyIndex(templateContent, keyPrefix, index);
                            continue;
                        }

                        var value = templateModel.GetValue(key);
                        var modifiersString = templateContent.Substring(keyEnd, endPos - keyEnd).Replace(".", string.Empty).Replace(TemplateKeyEnd, string.Empty);

                        contentToReplaceWith = ApplyModifiers(value, modifiersString);
                        partToReplaceLength = endPos - index;
                    }

                    // Replace template content by value
                    templateContent = templateContent.Remove(partToReplaceStart, partToReplaceLength);
                    templateContent = templateContent.Insert(partToReplaceStart, contentToReplaceWith);

                    Log.Debug($"Replaced template key '{keyPrefix}{key}' at position '{index}'");

                    index = FindNextKeyIndex(templateContent, keyPrefix, index);
                }
            }

            return templateContent;
        }

        private int FindNextKeyIndex(string content, string keyPrefix, int currentIndex)
        {
            return content.IndexOf(keyPrefix, currentIndex + 1, StringComparison.OrdinalIgnoreCase);
        }

        private string GenerateForeachContent(object scope, string foreachTemplate)
        {
            var template = new CollectionItemTemplate(scope);

            var value = ReplaceValues(foreachTemplate, template);
            return value;
        }

        private string ApplyModifiers(string value, string modifiersString)
        {
            var modifiers = modifiersString.Split(new[] { ModifierSeparator }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var modifier in modifiers)
            {
                value = ApplyModifier(value, modifier);
            }

            return value;
        }

        protected virtual string ApplyModifier(string value, string modifier)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            modifier = modifier.Trim(ModifierSeparator);

            if (modifier.EqualsIgnoreCase(Modifiers.Lowercase))
            {
                value = value.ToLowerInvariant();
            }
            else if (modifier.EqualsIgnoreCase(Modifiers.Uppercase))
            {
                value = value.ToUpperInvariant();
            }
            else if (modifier.EqualsIgnoreCase(Modifiers.Camelcase))
            {
                var character = value[0];
                value = value.Remove(0, 1);
                value = value.Insert(0, char.ToLower(character).ToString());
            }
            else
            {
                throw Log.ErrorAndCreateException<SolutionGeneratorException>($"Modifier '{modifier}' is not supported");
            }

            return value;
        }
    }
}