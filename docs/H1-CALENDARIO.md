# H1: calendario de la liga ficticia

Desde la raíz del repositorio, ejecuta:

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-calendar
```

Verás 14 jornadas de cuatro partidos. En la primera vuelta, los ocho clubes se enfrentan una vez entre sí. La segunda repite los cruces con la localía invertida. El orden es reproducible y consultar el calendario no escribe en la base de datos.

Para comprobar los cruces, las localías y las 56 citas de la temporada:

```powershell
dotnet test --filter FullyQualifiedName~DemoCalendarTests
```

## Plantillas ficticias

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-rosters
```

Cada club tiene 14 jugadores ficticios: dos porteros, tres cierres, cuatro alas, tres pívots y dos universales. La consulta no escribe en la base de datos ni importa los Excel.

## Validación de convocatorias

El validador de dominio exige cinco titulares (un portero y cuatro jugadores de campo). Titulares y suplentes deben ser distintos, pertenecer al club y estar disponibles. La validación recibe las sanciones y lesiones no aptas de ese partido; no modifica ni guarda la convocatoria.

Para comprobar los rechazos y una convocatoria válida:

```powershell
dotnet test --filter FullyQualifiedName~DemoSquadValidatorTests
```

La consola no permite editar convocatorias.

## Primer partido simulado

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-match 42
```

La consola selecciona y valida la convocatoria de cada club del primer partido del calendario. Muestra el marcador y los goleadores. La semilla es un entero sin signo de 64 bits. Con la misma versión, partido y semilla, la traza de eventos se repite sin usar la hora del equipo ni escribir en la base de datos.

La semilla de carrera se combina mediante XOR con FNV-1a de 64 bits sobre la jornada, el local y el visitante (texto UTF-8, separados por bytes nulos). SplitMix64 inicializa los flujos Xoshiro256** de acciones y resolución. Esta primera versión registra períodos, tiros y goles con probabilidades de prueba; aún no modela tácticas, sustituciones, faltas o lesiones durante el juego, ni guarda resultados.

## Temporada y clasificación

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-season 42
```

Simula los 56 partidos y muestra la clasificación final. Una victoria vale 3 puntos; un empate, 1; una derrota, 0. A igualdad de puntos, ordena por diferencia de goles, goles a favor y, por último, el identificador de club en orden ordinal ascendente. En esta demo fija el identificador es el nombre canónico del club.

El resultado de cada encuentro se conserva en memoria durante la ejecución.

### Jugar jornada a jornada

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-rounds 42
```

Pulsa **Intro** para simular solo la siguiente jornada y ver sus cuatro marcadores y la clasificación provisional. La tabla cuenta únicamente los partidos disputados; tras la jornada 14 es definitiva y coincide con la del modo de temporada completa para la misma semilla. Escribe `salir` para detenerte.

Para retomar manualmente después de dos jornadas, indica la misma semilla y el número de jornadas completadas:

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-rounds 42 2
```

La consola reconstruye la clasificación y la próxima pulsación de **Intro** juega la jornada 3.

Para guardar antes de salir, escribe `guardar progreso.db` en la consola (la ruta se escribe sin comillas, incluso si contiene espacios). El archivo debe ser nuevo. Guardar no juega ninguna jornada.

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-rounds-load progreso.db
```

La carga recupera la clasificación y deja preparada la siguiente jornada. El guardado por jornadas es SQLite y tiene su propio formato versionado: no sustituye a los archivos de carrera de temporadas completas.

La suite de `dotnet test` también comprueba este flujo en procesos separados. Para ejecutar solo esas pruebas: `dotnet test --filter FullyQualifiedName~HeadlessRoundCheckpointTests`.

## Carrera de diez temporadas

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-career 42
```

Cierra diez temporadas con el mismo calendario de clubes y muestra el palmarés: un campeón por temporada. El historial conserva los resultados y la clasificación de cada edición por separado. Repetir el cierre de una edición ya cerrada no añade otra entrada ni modifica su palmarés.

La primera temporada usa la semilla indicada. Para las siguientes se suma `(númeroDeTemporada − 1) × 0x9E3779B97F4A7C15` con aritmética de 64 bits sin signo. Este comando no escribe ningún archivo.

## Guardado manual de la carrera

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-career-save 42 2 carrera.db
dotnet run --project src/UniFutsal.Headless -- --demo-career-load carrera.db
```

El primer comando crea un archivo SQLite nuevo tras cerrar dos temporadas. El segundo recupera el palmarés y muestra la temporada 3 como siguiente. La carpeta de destino debe existir. Si el archivo ya existe, no se reemplaza: elige otro nombre.

Este guardado de demo admite de cero a diez temporadas cerradas. Guarda la versión del formato y del motor, la semilla, el progreso y una huella SHA-256; al cargar, reconstruye los resultados y comprueba que la huella coincida. No modifica el archivo al leerlo ni admite autoguardados, guardado durante un partido o migraciones.

### Avanzar un guardado

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-career-advance carrera.db carrera-t3.db
dotnet run --project src/UniFutsal.Headless -- --demo-career-load carrera-t3.db
```

El primer comando cierra la siguiente temporada, muestra su campeón y deja el nuevo progreso en otro archivo. El original conserva las dos temporadas anteriores. Si el origen es inválido, el destino ya existe o la carrera ha cerrado diez temporadas, no se publica un archivo.

### Recuperar desde una copia

Si el archivo principal está dañado, elige un guardado anterior y escribe la recuperación en un archivo nuevo:

```powershell
dotnet run --project src/UniFutsal.Headless -- --demo-career-recover carrera-copia.db carrera-recuperada.db
dotnet run --project src/UniFutsal.Headless -- --demo-career-load carrera-recuperada.db
```

Se valida la copia antes de crear el nuevo archivo. La recuperación conserva exactamente el progreso de la copia, que puede ser anterior al del archivo dañado. No se buscan otras copias ni se sobrescribe ningún archivo.
