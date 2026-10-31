namespace SlimeGoals.Core.Validation;

/// <summary>Erro de validação ligado a um campo (Field = nameof da propriedade do rascunho).</summary>
public sealed record ValidationError(string Field, string Message);
