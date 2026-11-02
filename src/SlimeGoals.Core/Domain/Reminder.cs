namespace SlimeGoals.Core.Domain;

/// <summary>Texto de uma notificação de lembrete (spec §9.11). <paramref name="PrimaryLabel"/> é o rótulo da ação principal.</summary>
public sealed record ReminderText(string Title, string Body, string PrimaryLabel);

/// <summary>Lembrete disparado: o id do registro no log, a meta e o texto a mostrar.</summary>
public sealed record Reminder(Guid LogId, Guid GoalId, ReminderText Text);

/// <summary>Ação escolhida pelo usuário na notificação. <see cref="Open"/> é o clique no corpo.</summary>
public enum ReminderAction { Primary, Snooze, Ignore, Open }

/// <summary>Resultado de tratar uma ação: concluída, abrir o app, ou a notificação já não vale.</summary>
public enum ReminderOutcome { Handled, OpenApp, Stale }
