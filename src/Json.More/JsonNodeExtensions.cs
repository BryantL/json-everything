﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Json.More;

/// <summary>
/// Provides extension functionality for <see cref="JsonNode"/>.
/// </summary>
public static class JsonNodeExtensions
{
	private static readonly JsonSerializerOptions _unfriendlyCharSerialization = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>
	/// Determines JSON-compatible equivalence.
	/// </summary>
	/// <param name="a">The first element.</param>
	/// <param name="b">The second element.</param>
	/// <returns>`true` if the element are equivalent; `false` otherwise.</returns>
	/// <remarks>
	/// <see cref="JsonNode.DeepEquals(JsonNode,JsonNode)"/> has trouble testing numeric
	/// equality when `decimal` is involved.  As such, it is still advised to use this
	/// method instead.  See https://github.com/dotnet/runtime/issues/97490.
	/// </remarks>
	public static bool IsEquivalentTo(this JsonNode? a, JsonNode? b)
	{
		switch (a, b)
		{
			case (null, null):
				return true;
			case (JsonObject objA, JsonObject objB):
				if (objA.Count != objB.Count) return false;
				var grouped = objA.Concat(objB)
					.GroupBy(p => p.Key)
					.Select(g => g.ToList())
					.ToList();
				return grouped.All(g => g.Count == 2 && g[0].Value.IsEquivalentTo(g[1].Value));
			case (JsonArray arrayA, JsonArray arrayB):
				if (arrayA.Count != arrayB.Count) return false;
				var zipped = arrayA.Zip(arrayB, (ae, be) => (ae, be));
				return zipped.All(p => p.ae.IsEquivalentTo(p.be));
			case (JsonValue aValue, JsonValue bValue):
				var aNumber = aValue.GetNumber();
				var bNumber = bValue.GetNumber();
				if (aNumber != null) return aNumber == bNumber;

				var aString = aValue.GetString();
				var bString = bValue.GetString();
				if (aString != null) return aString == bString;

				var aBool = aValue.GetBool();
				var bBool = bValue.GetBool();
				if (aBool.HasValue) return aBool == bBool;

				var aObj = aValue.GetValue<object>();
				var bObj = bValue.GetValue<object>();
				if (aObj is JsonElement aElement && bObj is JsonElement bElement)
					return aElement.IsEquivalentTo(bElement);

				return aObj.Equals(bObj);
			default:
				return false;
		}
	}

	// source: https://stackoverflow.com/a/60592310/878701, modified for netstandard2.0
	// license: https://creativecommons.org/licenses/by-sa/4.0/
	/// <summary>
	/// Generate a consistent JSON-value-based hash code for the element.
	/// </summary>
	/// <param name="node">The element.</param>
	/// <param name="maxHashDepth">Maximum depth to calculate.  Default is -1 which utilizes the entire structure without limitation.</param>
	/// <returns>The hash code.</returns>
	/// <remarks>
	/// See the following for discussion on why the default implementation is insufficient:
	///
	/// - https://github.com/json-everything/json-everything/issues/76
	/// - https://github.com/dotnet/runtime/issues/33388
	/// </remarks>
	public static int GetEquivalenceHashCode(this JsonNode node, int maxHashDepth = -1)
	{
		static void Add(ref int current, object? newValue)
		{
			unchecked
			{
				current = current * 397 ^ (newValue?.GetHashCode() ?? 0);
			}
		}

		void ComputeHashCode(JsonNode? target, ref int current, int depth)
		{
			if (target == null) return;

			Add(ref current, target.GetType());

			switch (target)
			{
				case JsonArray array:
					if (depth != maxHashDepth)
						foreach (var item in array)
							ComputeHashCode(item, ref current, depth + 1);
					else
						Add(ref current, array.Count);
					break;

				case JsonObject obj:
					foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
					{
						Add(ref current, property.Key);
						if (depth != maxHashDepth)
							ComputeHashCode(property.Value, ref current, depth + 1);
					}
					break;
				default:
					var value = target.AsValue();
					if (value.TryGetValue<bool>(out var boolA))
						Add(ref current, boolA);
					else
					{
						var number = value.GetNumber();
						if (number != null)
							Add(ref current, number);
						else if (value.TryGetValue<string>(out var stringA))
							Add(ref current, stringA);
					}

					break;
			}
		}

		var hash = 0;
		ComputeHashCode(node, ref hash, 0);
		return hash;
	}

	/// <summary>
	/// Gets JSON string representation for <see cref="JsonNode"/>, including null support.
	/// </summary>
	/// <param name="node">A node.</param>
	/// <param name="options">Serializer options</param>
	/// <returns>JSON string representation.</returns>
	public static string AsJsonString(this JsonNode? node, JsonSerializerOptions? options = null)
	{
		return node?.ToJsonString(options) ?? "null";
	}

	/// <summary>
	/// Gets a node's underlying numeric value.
	/// </summary>
	/// <param name="value">A JSON value.</param>
	/// <returns>Gets the underlying numeric value, or null if the node represented a non-numeric value.</returns>
	public static decimal? GetNumber(this JsonValue value)
	{
		if (value.TryGetValue(out JsonElement e))
		{
			if (e.ValueKind != JsonValueKind.Number) return null;
			return e.GetDecimal();
		}

		var number = GetInteger(value);
		if (number != null) return number;

		if (value.TryGetValue(out float f)) return (decimal)f;
		if (value.TryGetValue(out double d)) return (decimal)d;
		if (value.TryGetValue(out decimal dc)) return dc;

		return null;
	}

	/// <summary>
	/// Gets a node's underlying numeric value if it's an integer.
	/// </summary>
	/// <param name="value">A JSON value.</param>
	/// <returns>Gets the underlying numeric value if it's an integer, or null if the node represented a non-integer value.</returns>
	public static long? GetInteger(this JsonValue value)
	{
		if (value.TryGetValue(out JsonElement e))
		{
			if (e.ValueKind != JsonValueKind.Number) return null;
			var d = e.GetDecimal();
			if (d == Math.Floor(d)) return (long)d;
			return null;
		}
		if (value.TryGetValue(out byte b)) return b;
		if (value.TryGetValue(out sbyte sb)) return sb;
		if (value.TryGetValue(out short s)) return s;
		if (value.TryGetValue(out ushort us)) return us;
		if (value.TryGetValue(out int i)) return i;
		if (value.TryGetValue(out uint ui)) return ui;
		if (value.TryGetValue(out long l)) return l;
		// this doesn't feel right... throw?
		if (value.TryGetValue<ulong>(out _))
			throw new NotSupportedException("Unsigned longs cannot be supported with this method.  A separate check will need to be used.");

		return null;
	}

	/// <summary>
	/// Gets a node's underlying string value.
	/// </summary>
	/// <param name="value">A JSON value.</param>
	/// <returns>Gets the underlying string value, or null.</returns>
	/// <remarks>
	/// JsonNode may use a <see cref="JsonElement"/> under the hood which subsequently contains a string.
	/// This means that `JsonNode.GetValue&lt;string&gt;()` will not work as expected.
	/// </remarks>
	public static string? GetString(this JsonValue value)
	{
		if (value.TryGetValue(out JsonElement e))
		{
			if (e.ValueKind != JsonValueKind.String) return null;
			return e.GetString();
		}

		if (value.TryGetValue(out string? s)) return s;
		if (value.TryGetValue(out char c)) return c.ToString();

		return value.GetValueKind() == JsonValueKind.String
			? value.ToJsonString()[1..^1] //strip JSON literal double quotes
			: null;
	}

	/// <summary>
	/// Gets a node's underlying boolean value.
	/// </summary>
	/// <param name="value">A JSON value.</param>
	/// <returns>Gets the underlying boolean value, or null.</returns>
	/// <remarks>
	/// JsonNode may use a <see cref="JsonElement"/> under the hood which subsequently contains a boolean.
	/// This means that `JsonNode.GetValue&lt;bool&gt;()` will not work as expected.
	/// </remarks>
	public static bool? GetBool(this JsonValue value)
	{
		if (value.TryGetValue(out JsonElement e))
		{
			if (e.ValueKind == JsonValueKind.True) return true;
			if (e.ValueKind == JsonValueKind.False) return false;

			return null;
		}

		if (value.TryGetValue(out bool b)) return b;

		return null;
	}

	/// <summary>
	/// Convenience method that wraps <see cref="JsonObject.TryGetPropertyValue(string, out JsonNode?)"/>
	/// and catches argument exceptions.
	/// </summary>
	/// <param name="obj">The JSON object.</param>
	/// <param name="propertyName">The property name</param>
	/// <param name="node">The node under the property name if it exists and is singular; null otherwise.</param>
	/// <param name="e">An exception if one was thrown during the access attempt.</param>
	/// <returns>true if the property exists and is singular within the JSON data.</returns>
	/// <remarks>
	/// <see cref="JsonObject.TryGetPropertyValue(string, out JsonNode?)"/> throws an
	/// <see cref="ArgumentException"/> if the node was parsed from data that has duplicate
	/// keys.  Please see https://github.com/dotnet/runtime/issues/70604 for more information.
	/// </remarks>
	public static bool TryGetValue(this JsonObject obj, string propertyName, out JsonNode? node, out Exception? e)
	{
		e = null;
		try
		{
			return obj.TryGetPropertyValue(propertyName, out node);
		}
		catch (ArgumentException ae)
		{
			e = ae;
			node = null;
			return false;
		}
	}

	/// <summary>
	/// Creates a new <see cref="JsonArray"/> by copying from an enumerable of nodes.
	/// </summary>
	/// <param name="nodes">The nodes.</param>
	/// <returns>A JSON array.</returns>
	/// <remarks>
	///	`JsonNode` may only be part of a single JSON tree, i.e. have a single parent.
	/// Copying a node allows its value to be saved to another JSON tree.
	/// </remarks>
	public static JsonArray ToJsonArray(this IEnumerable<JsonNode?> nodes)
	{
		return new JsonArray(nodes.Select(x => x?.DeepClone()).ToArray());
	}

	///  <summary>
	///  Gets a JSON Path string that indicates the node's location within
	///  its JSON structure.
	///  </summary>
	///  <param name="node">The node to find.</param>
	///  <param name="useShorthand">Determines whether shorthand syntax is used when possible, e.g. `$.foo`.</param>
	///  <exception cref="ArgumentNullException">Null nodes cannot be located as the parent cannot be determined.</exception>
	///  <returns>
	/// 	A string containing a JSON Path.
	///  </returns>
	public static string GetPathFromRoot(this JsonNode node, bool useShorthand = false)
	{
		var current = node ?? throw new ArgumentNullException(nameof(node), "null nodes cannot be located");

		var segments = GetSegments(current);

		var sb = new StringBuilder();
		sb.Append('$');
		segments.Pop();  // first is always null - the root
		while (segments.Count != 0)
		{
			var segment = segments.Pop();
			var index = segment?.GetNumber();
			sb.Append(index != null ? $"[{index}]" : GetNamedSegmentForPath(segment!, useShorthand));
		}

		return sb.ToString();
	}

	private static Stack<JsonValue?> GetSegments(JsonNode? current)
	{
		var segments = new Stack<JsonValue?>();
		while (current != null)
		{
			var segment = current.Parent switch
			{
				null => null,
				JsonObject obj => GetKey(obj, current),
				JsonArray arr => GetIndex(arr, current),
#pragma warning disable CA2208
				_ => throw new ArgumentOutOfRangeException("parent", "this shouldn't happen")
#pragma warning restore CA2208
			};
			segments.Push(segment);
			current = current.Parent;
		}

		return segments;
	}

	private static JsonValue GetKey(JsonObject obj, JsonNode current)
	{
		return JsonValue.Create(obj.First(x => ReferenceEquals(x.Value, current)).Key)!;
	}

	private static JsonValue GetIndex(JsonArray arr, JsonNode current)
	{
		return JsonValue.Create(arr.IndexOf(current));
	}

	private static readonly Regex _pathSegmentTestPattern = new("^[a-z][a-z_]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static string GetNamedSegmentForPath(JsonValue segment, bool useShorthand)
	{
		var value = segment.GetValue<string>();
		if (useShorthand && _pathSegmentTestPattern.IsMatch(value))  return $".{value}";

		return $"['{PrepForJsonPath(segment.AsJsonString(_unfriendlyCharSerialization))}']";
	}

	// pass JSON string because it will handle char escaping inside the string.
	// just need to replace the quotes.
	private static string PrepForJsonPath(string jsonString)
	{
		var content = jsonString[1..^1];
		var escaped = content.Replace("\\\"", "\"")
			.Replace("'", "\\'");
		return escaped;
	}

	///  <summary>
	///  Gets a JSON Pointer string that indicates the node's location within
	///  its JSON structure.
	///  </summary>
	///  <param name="node">The node to find.</param>
	///  <exception cref="ArgumentNullException">Null nodes cannot be located as the parent cannot be determined.</exception>
	///  <returns>
	/// 	A string containing a JSON Pointer.
	///  </returns>
	public static string GetPointerFromRoot(this JsonNode node)
	{
		var current = node ?? throw new ArgumentNullException(nameof(node), "null nodes cannot be located");

		var segments = GetSegments(current);

		var sb = new StringBuilder();
		segments.Pop();  // first is always null - the root
		while (segments.Count != 0)
		{
			var segment = segments.Pop();
			var index = segment?.GetNumber();
			sb.Append(index != null ? $"/{index}" : $"/{PrepForJsonPointer(segment!.GetValue<string>())}");
		}

		return sb.ToString();
	}

	private static string PrepForJsonPointer(string s)
	{
		var escaped = s.Replace("~", "~0")
			.Replace("/", "~1");
		return escaped;
	}
}