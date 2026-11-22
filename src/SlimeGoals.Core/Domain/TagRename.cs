namespace SlimeGoals.Core.Domain;

/// <summary>Uma renomeação (ou junção) feita neste aparelho, com os nomes já normalizados.</summary>
public readonly record struct TagRename(string OldName, string NewName);
