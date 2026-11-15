// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Markup;

/// <summary>
/// Evaluates the predicate of a conditional XAML namespace, for example
/// <c>IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)</c>.
/// </summary>
/// <param name="condition">The raw text after the <c>?</c> of the namespace.</param>
/// <param name="resolveNamespace">
/// Returns the namespace bound to an <c>xmlns</c> prefix where the conditional namespace is declared, or
/// <see langword="null"/> when the prefix is unknown. Custom conditions (<c>prefix:ConditionType(argument)</c>) need it.
/// </param>
/// <returns>
/// Whether the predicate holds, or <see langword="null"/> when it cannot be evaluated; its members are then left untouched.
/// </returns>
internal delegate bool? XamlConditionEvaluator(string condition, Func<string, string?> resolveNamespace);
