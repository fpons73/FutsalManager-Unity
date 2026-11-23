using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UniFutsal.Application.Diagnostics;
using UniFutsal.Domain.Competitions;
using UniFutsal.Infrastructure.Careers;
using UniFutsal.Infrastructure.Diagnostics;

if (args is ["--demo-calendar", "--help"])
{
    Console.WriteLine("Uso: --demo-calendar (muestra el calendario de la liga ficticia)");
    return 0;
}

if (args is ["--demo-calendar"])
{
    Console.WriteLine("Calendario de la liga ficticia");
    var currentRound = 0;
    foreach (var fixture in DemoCalendar.Create())
    {
        if (fixture.Round != currentRound)
        {
            currentRound = fixture.Round;
            Console.WriteLine($"Jornada {currentRound}");
        }

        Console.WriteLine($"  {fixture.HomeClub} - {fixture.AwayClub}");
    }

    return 0;
}

if (args is ["--demo-rosters", "--help"])
{
    Console.WriteLine("Uso: --demo-rosters (muestra las plantillas de la liga ficticia)");
    return 0;
}

if (args is ["--demo-rosters"])
{
    Console.WriteLine("Plantillas de la liga ficticia");
    foreach (var roster in DemoRoster.Create().GroupBy(player => player.Club))
    {
        Console.WriteLine(roster.Key);
        foreach (var player in roster)
        {
            var role = player.Role switch
            {
                DemoPlayerRole.Goalkeeper => "Portero",
                DemoPlayerRole.Defender => "Cierre",
                DemoPlayerRole.Winger => "Ala",
                DemoPlayerRole.Pivot => "Pívot",
                DemoPlayerRole.Universal => "Universal",
                _ => throw new ArgumentOutOfRangeException()
            };
            Console.WriteLine($"  {player.Name} - {role}");
        }
    }

    return 0;
}

if (args.Length > 0 && args[0] == "--demo-match")
{
    const string usage = "Uso: --demo-match <semilla> (simula el primer partido ficticio)";
    if (args is ["--demo-match", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (args.Length != 2 || !ulong.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
    {
        Console.Error.WriteLine("La semilla debe ser un entero sin signo de 64 bits.");
        Console.Error.WriteLine(usage);
        return 2;
    }

    var fixture = DemoCalendar.Create()[0];
    var roster = DemoRoster.Create();
    var match = DemoMatch.Simulate(fixture, seed, SelectSquad(fixture.HomeClub), SelectSquad(fixture.AwayClub));
    if (!match.Completed)
    {
        Console.Error.WriteLine("No se pudo simular el partido: las convocatorias ficticias no son válidas.");
        return 1;
    }

    Console.WriteLine($"Simulación ficticia | Semilla: {seed}");
    Console.WriteLine($"{fixture.HomeClub} {match.HomeGoals} - {match.AwayGoals} {fixture.AwayClub}");
    var names = roster.ToDictionary(player => player.Id, player => player.Name, StringComparer.Ordinal);
    foreach (var goal in match.Events.Where(item => item.Kind == DemoMatchEventKind.Goal))
    {
        if (goal.PlayerId is { } scorer)
            Console.WriteLine($"  {goal.ElapsedEffectiveMs / 60_000:D2}:{goal.ElapsedEffectiveMs / 1_000 % 60:D2} | {goal.Club}: {names[scorer]}");
    }
    return 0;

    DemoMatchSquad SelectSquad(string club)
    {
        return DemoMatchSquad.SelectDefault(roster.Where(player => player.Club == club));
    }
}

if (args.Length > 0 && args[0] == "--demo-season")
{
    const string usage = "Uso: --demo-season <semilla> (simula la temporada ficticia y muestra la clasificación)";
    if (args is ["--demo-season", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (args.Length != 2 || !ulong.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seasonSeed))
    {
        Console.Error.WriteLine("La semilla debe ser un entero sin signo de 64 bits.");
        Console.Error.WriteLine(usage);
        return 2;
    }

    var season = DemoSeason.Simulate(seasonSeed);
    Console.WriteLine($"Temporada ficticia | Semilla: {seasonSeed} | Partidos: {season.Results.Count}");
    PrintStandings(season.Standings);
    return 0;
}

if (args.Length > 0 && args[0] == "--demo-career")
{
    const string usage = "Uso: --demo-career <semilla> (cierra diez temporadas y muestra el palmarés ficticio)";
    if (args is ["--demo-career", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (args.Length != 2 || !ulong.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var careerSeed))
    {
        Console.Error.WriteLine("La semilla debe ser un entero sin signo de 64 bits.");
        Console.Error.WriteLine(usage);
        return 2;
    }

    var career = new DemoCareer(careerSeed);
    for (var number = 1; number <= 10; number++)
        career = career.CompleteSeason(number);

    Console.WriteLine($"Carrera ficticia | Semilla: {career.Seed} | Partidos: {career.CompletedSeasons.Sum(item => item.Result.Results.Count)}");
    foreach (var item in career.CompletedSeasons)
        Console.WriteLine($"Temporada {item.Number}: {item.Champion} | Partidos: {item.Result.Results.Count}");
    Console.WriteLine($"Siguiente temporada: {career.NextSeasonNumber}");
    return 0;
}

if (args.Length > 0 && args[0] is "--demo-career-save" or "--demo-career-load")
{
    var save = args[0] == "--demo-career-save";
    var usage = save
        ? "Uso: --demo-career-save <semilla> <temporadas cerradas: 0–10> <archivo>"
        : "Uso: --demo-career-load <archivo>";
    if (args is [_, "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    ulong seed = 0;
    var closed = 0;
    if (save
        ? args.Length != 4 || !ulong.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out seed) ||
          !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out closed) || closed > SqliteDemoCareerStore.MaxClosedSeasons
        : args.Length != 2)
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    try
    {
        var careerStore = new SqliteDemoCareerStore();
        if (save)
        {
            var career = new DemoCareer(seed);
            for (var number = 1; number <= closed; number++)
                career = career.CompleteSeason(number);

            careerStore.SaveNew(args[3], career);
            Console.WriteLine("Carrera guardada en un archivo nuevo.");
        }
        else
        {
            var career = careerStore.Load(args[1]);
            Console.WriteLine($"Carrera cargada | Semilla: {career.Seed}");
            foreach (var season in career.CompletedSeasons)
                Console.WriteLine($"Temporada {season.Number}: {season.Champion}");
            Console.WriteLine($"Siguiente temporada: {career.NextSeasonNumber}");
        }
        return 0;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
    {
        Console.Error.WriteLine(save
            ? "No se pudo guardar la carrera en un archivo nuevo. Comprueba la ruta y que el archivo no exista."
            : "No se pudo cargar la carrera. Comprueba la ruta y que el guardado sea válido y compatible.");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "--demo-career-advance")
{
    const string usage = "Uso: --demo-career-advance <archivo de origen> <archivo nuevo>";
    if (args is ["--demo-career-advance", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (args.Length != 3)
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    try
    {
        var completed = new SqliteDemoCareerStore().AdvanceToNewSave(args[1], args[2]);
        if (completed == null)
        {
            Console.Error.WriteLine($"La carrera ya alcanzó el límite de {SqliteDemoCareerStore.MaxClosedSeasons} temporadas cerradas.");
            return 1;
        }

        Console.WriteLine($"Temporada {completed.Number}: {completed.Champion}. Progreso guardado en un archivo nuevo.");
        Console.WriteLine($"Siguiente temporada: {completed.Number + 1}");
        return 0;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
    {
        Console.Error.WriteLine("No se pudo avanzar la carrera. Comprueba que el origen sea válido y que el destino no exista.");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "--demo-career-recover")
{
    const string usage = "Uso: --demo-career-recover <copia de seguridad> <archivo nuevo>";
    if (args is ["--demo-career-recover", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (args.Length != 3)
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    try
    {
        var recovered = new SqliteDemoCareerStore().RecoverToNewSave(args[1], args[2]);
        Console.WriteLine($"Carrera recuperada desde la copia indicada | Temporadas cerradas: {recovered.CompletedSeasons.Count}");
        Console.WriteLine($"Siguiente temporada: {recovered.NextSeasonNumber}");
        return 0;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
    {
        Console.Error.WriteLine("No se pudo recuperar la carrera. Comprueba que la copia sea válida y que el destino no exista.");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "--demo-rounds")
{
    const string usage = "Uso: --demo-rounds <semilla> [jornadas completadas: 0–14]";
    if (args is ["--demo-rounds", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }

    if (args.Length is < 2 or > 3 ||
        !ulong.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var roundsSeed))
    {
        Console.Error.WriteLine("La semilla debe ser un entero sin signo de 64 bits.");
        Console.Error.WriteLine(usage);
        return 2;
    }

    var completedRounds = 0;
    if (args.Length == 3 && !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out completedRounds))
    {
        Console.Error.WriteLine("El número de jornadas completadas debe ser un entero no negativo.");
        return 2;
    }

    DemoRoundSeason progress;
    try
    {
        progress = DemoRoundSeason.Resume(roundsSeed, completedRounds);
    }
    catch (ArgumentOutOfRangeException)
    {
        Console.Error.WriteLine("El número de jornadas completadas supera el total de la temporada.");
        return 2;
    }

    return RunRoundMode(progress);
}

if (args.Length > 0 && args[0] == "--demo-rounds-load")
{
    const string usage = "Uso: --demo-rounds-load <archivo>";
    if (args is ["--demo-rounds-load", "--help"])
    {
        Console.WriteLine(usage);
        return 0;
    }
    if (args.Length != 2)
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    DemoRoundSeason progress;
    try
    {
        progress = new SqliteDemoCareerStore().LoadRoundCheckpoint(args[1]);
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
    {
        Console.Error.WriteLine("No se pudo cargar el progreso por jornadas. Comprueba el archivo y su compatibilidad.");
        return 1;
    }
    return RunRoundMode(progress);
}

static int RunRoundMode(DemoRoundSeason progress)
{
    Console.WriteLine($"Temporada ficticia por jornadas | Semilla: {progress.Seed}");
    if (progress.CompletedRounds > 0)
    {
        Console.WriteLine($"Jornadas retomadas: {progress.CompletedRounds} de {progress.RoundCount}");
        Console.WriteLine(progress.IsComplete ? "Clasificación final" : "Clasificación provisional");
        PrintStandings(progress.Standings);
    }

    while (!progress.IsComplete)
    {
        Console.WriteLine("Pulsa Intro para jugar, escribe guardar <archivo> o salir.");
        var input = Console.ReadLine();
        var command = input?.Trim();
        if (command == null || command.Equals("salir", StringComparison.OrdinalIgnoreCase))
            break;
        if (command.Equals("guardar", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Uso: guardar <archivo nuevo>");
            continue;
        }
        if (command.StartsWith("guardar ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                new SqliteDemoCareerStore().SaveRoundCheckpointNew(command.Substring("guardar ".Length).Trim(), progress);
                Console.WriteLine($"Progreso guardado | Jornadas completadas: {progress.CompletedRounds}");
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
            {
                Console.Error.WriteLine("No se pudo guardar el progreso. Comprueba la ruta y que el archivo no exista.");
            }
            continue;
        }
        if (input!.Length != 0)
        {
            Console.WriteLine("Entrada no reconocida. No se jugó ninguna jornada.");
            continue;
        }

        var matches = progress.PlayNextRound();
        Console.WriteLine($"Jornada {progress.CompletedRounds} de {progress.RoundCount}");
        foreach (var match in matches)
            Console.WriteLine($"  {match.Fixture.HomeClub} {match.HomeGoals} - {match.AwayGoals} {match.Fixture.AwayClub}");
        Console.WriteLine(progress.IsComplete ? "Clasificación final" : "Clasificación provisional");
        PrintStandings(progress.Standings);
    }

    Console.WriteLine($"Jornadas completadas: {progress.CompletedRounds} de {progress.RoundCount}");
    return 0;
}

if (args.Length != 0)
{
    Console.Error.WriteLine("Argumentos no reconocidos. Ejecuta sin argumentos para el diagnóstico local.");
    return 2;
}

var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "UniFutsalManager");
var dbPath = Path.Combine(appDataDir, "h0_diagnostic.db");

var store = new SqliteLocalDiagnosticStore(dbPath);
var useCase = new RunLocalDiagnosticUseCase(
    store: store,
    utcClock: () => DateTime.UtcNow,
    idGenerator: () => "diag-" + Guid.NewGuid().ToString("N").Substring(0, 8));

var result = useCase.Execute("0.1.0-h0");

if (result.IsAvailable)
{
    Console.WriteLine("Datos locales disponibles");
    Console.WriteLine($"Registro verificado: {result.Record.Id}");
    Console.WriteLine($"Base de datos: {dbPath}");
    return 0;
}

Console.WriteLine($"No se pudieron verificar los datos locales ({result.ErrorCode}).");
return 1;

static void PrintStandings(IReadOnlyList<DemoStanding> standings)
{
    Console.WriteLine($"{"#",2}  {"Club",-20} {"PJ",3} {"PG",3} {"PE",3} {"PP",3} {"GF",3} {"GC",3} {"DG",3} {"Pts",3}");
    var position = 0;
    foreach (var row in standings)
        Console.WriteLine($"{++position,2}  {row.Club,-20} {row.Played,3} {row.Wins,3} {row.Draws,3} {row.Losses,3} {row.GoalsFor,3} {row.GoalsAgainst,3} {row.GoalDifference,3} {row.Points,3}");
}
