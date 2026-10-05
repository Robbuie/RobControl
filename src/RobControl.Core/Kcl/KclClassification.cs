namespace RobControl.Core.Kcl;

/// <summary>The verdict on one command, with the reason in words the person will read.</summary>
public sealed record KclClassification(KclCommandClass Class, string Normalised, string Verb, string Reason);
