"""Report-only validation for the local futsal encyclopedia workbooks."""

import argparse
import csv
import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from unicodedata import normalize

from openpyxl import load_workbook


CLUBS_FILE = "Enciclopedia_Futsal_Equipas_Masculino1.xlsx"
COACHES_FILE = "Enciclopedia_Futsal_Entrenadores_Masculino1.xlsx"
PLAYERS_FILE = re.compile(r"Enciclopedia_Futsal_Jogadores_Activos_Masculino[0-9]+\.xlsx", re.IGNORECASE)
VALID_ID = re.compile(r"[0-9]{1,20}")
MISSING_CLUB_LABELS = {"", "-", "—", "n/a", "none", "sem clube", "sem equipa", "sin equipo"}
MEMBERSHIP_STATES = ("one_club_candidate", "ambiguous_club", "unmatched_club", "no_club", "conflicting_club_labels")


class ValidationError(Exception):
    """An input failure safe to print without exposing cells."""


def text(value):
    return "" if value is None else str(value).strip()


def name_key(value):
    return " ".join(normalize("NFKC", text(value)).casefold().split())


def read_rows(path, sheet_name, fields):
    book = None
    try:
        book = load_workbook(path, read_only=True, data_only=True, keep_links=False)
        if sheet_name not in book.sheetnames:
            raise ValidationError(f"Falta la hoja {sheet_name} en {path.name}.")
        rows = iter(book[sheet_name].values)
        header = tuple(text(value) for value in next(rows, ()))
        if any(header.count(field) != 1 for field in fields):
            raise ValidationError(f"Faltan columnas requeridas o hay encabezados duplicados en {path.name}.")
        indices = tuple(header.index(field) for field in fields)
        for row in rows:
            if any(text(value) for value in row):
                yield tuple(row[index] if index < len(row) else None for index in indices)
    except ValidationError:
        raise
    except Exception as error:
        raise ValidationError(f"No se pudo leer el Excel {path.name}.") from error
    finally:
        if book is not None:
            book.close()


def group_records(paths, sheet_name, label_field, ignored_labels=()):
    labels_by_id = defaultdict(set)
    stats = dict(rows=0, unique_ids=0, duplicate_id_rows=0, missing_id_rows=0, invalid_id_rows=0)
    for path in paths:
        for raw_id, raw_label in read_rows(path, sheet_name, ("ID", label_field)):
            stats["rows"] += 1
            raw_id = text(raw_id)
            if not raw_id:
                stats["missing_id_rows"] += 1
                continue
            if not VALID_ID.fullmatch(raw_id) or int(raw_id) == 0:
                stats["invalid_id_rows"] += 1
                continue
            entity_id = int(raw_id)
            if entity_id in labels_by_id:
                stats["duplicate_id_rows"] += 1
            labels = labels_by_id[entity_id]
            label = name_key(raw_label)
            if label and label not in ignored_labels:
                labels.add(label)
    stats["unique_ids"] = len(labels_by_id)
    return stats, labels_by_id


def classify_memberships(labels_by_id, clubs_by_name):
    stats = dict.fromkeys(MEMBERSHIP_STATES, 0)
    for labels in labels_by_id.values():
        if not labels:
            state = "no_club"
        elif len(labels) > 1:
            state = "conflicting_club_labels"
        else:
            candidates = clubs_by_name.get(next(iter(labels)), ())
            state = ("unmatched_club" if not candidates
                     else "one_club_candidate" if len(candidates) == 1
                     else "ambiguous_club")
        stats[state] += 1
    return stats


def read_reviewed_links(path, people_by_kind, club_labels, clubs_by_name):
    reviewed = {kind: set() for kind in people_by_kind}
    try:
        with path.open(encoding="utf-8-sig", newline="") as source:
            rows = csv.reader(source, strict=True)
            if next(rows, None) != ["kind", "person_id", "team_label", "club_id"]:
                raise ValidationError("La cabecera del CSV debe ser kind,person_id,team_label,club_id.")
            for row in rows:
                if not row:
                    continue
                line = rows.line_num
                if len(row) != 4:
                    raise ValidationError(f"CSV, línea {line}: se requieren cuatro campos.")
                kind, person, label, club = (value.strip() for value in row)
                if kind not in people_by_kind:
                    raise ValidationError(f"CSV, línea {line}: tipo de persona no válido.")
                if not all(VALID_ID.fullmatch(value) and int(value) > 0 for value in (person, club)):
                    raise ValidationError(f"CSV, línea {line}: ID no válido.")
                person_id, club_id = int(person), int(club)
                if person_id in reviewed[kind]:
                    raise ValidationError(f"CSV, línea {line}: decisión duplicada.")
                labels = people_by_kind[kind].get(person_id)
                if labels is None:
                    raise ValidationError(f"CSV, línea {line}: persona no encontrada.")
                if len(labels) > 1:
                    raise ValidationError(f"CSV, línea {line}: nombres de equipo contradictorios.")
                label = name_key(label)
                if not labels or label not in labels:
                    raise ValidationError(f"CSV, línea {line}: el nombre de equipo no coincide con el Excel.")
                if not club_labels.get(club_id):
                    raise ValidationError(f"CSV, línea {line}: club no disponible en el catálogo.")
                candidates = clubs_by_name.get(label)
                if candidates and club_id not in candidates:
                    raise ValidationError(f"CSV, línea {line}: el club no está entre los candidatos.")
                reviewed[kind].add(person_id)
    except (OSError, UnicodeError, csv.Error) as error:
        raise ValidationError("No se pudo leer el CSV de revisión. Comprueba la ruta y el formato UTF-8.") from error
    return reviewed


def memberships_after_review(labels_by_id, clubs_by_name, reviewed):
    unresolved = {person_id: labels for person_id, labels in labels_by_id.items() if person_id not in reviewed}
    result = classify_memberships(unresolved, clubs_by_name)
    result["manually_reviewed_club"] = len(reviewed)
    return result


def validate(folder, reviewed_links=None):
    clubs_file = folder / CLUBS_FILE
    coaches_file = folder / COACHES_FILE
    player_files = sorted(path for path in folder.glob("*.xlsx") if PLAYERS_FILE.fullmatch(path.name) and path.is_file())
    if not clubs_file.is_file() or not coaches_file.is_file() or not player_files:
        raise ValidationError("Faltan los Excel esperados de clubes, entrenadores o jugadores.")

    club_stats, club_labels = group_records([clubs_file], "Equipas", "Nombre")
    if not club_labels:
        raise ValidationError("El Excel de clubes no contiene identificadores válidos.")
    clubs_by_name = defaultdict(set)
    for club_id, names in club_labels.items():
        for name in names:
            clubs_by_name[name].add(club_id)
    if not clubs_by_name:
        raise ValidationError("El Excel de clubes no contiene nombres utilizables.")

    coach_stats, coach_labels = group_records([coaches_file], "Treinadores", "Equipo Actual", MISSING_CLUB_LABELS)
    player_stats, player_labels = group_records(player_files, "Jogadores", "Equipo Actual", MISSING_CLUB_LABELS)
    reviewed = (read_reviewed_links(reviewed_links, {"coach": coach_labels, "player": player_labels},
                                    club_labels, clubs_by_name)
                if reviewed_links is not None else None)

    def memberships(kind, labels):
        if reviewed is None:
            return classify_memberships(labels, clubs_by_name)
        return memberships_after_review(labels, clubs_by_name, reviewed[kind])

    return {
        "schema": "unifutsal.real-data-validation/v1",
        "clubs": {"files": 1, "records": club_stats,
                  "ambiguous_name_keys": sum(len(ids) > 1 for ids in clubs_by_name.values())},
        "coaches": {"files": 1, "records": coach_stats,
                    "memberships": memberships("coach", coach_labels)},
        "players": {"files": len(player_files), "records": player_stats,
                    "memberships": memberships("player", player_labels)},
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description="Valida los Excel locales de fútbol sala sin importar ni modificar datos.")
    parser.add_argument("folder", type=Path, metavar="carpeta", help="carpeta que contiene los Excel originales")
    parser.add_argument("--reviewed-links", type=Path, metavar="CSV", help="decisiones manuales locales por ID de persona")
    args = parser.parse_args(argv)
    try:
        report = validate(args.folder, args.reviewed_links)
    except ValidationError as error:
        print(error, file=sys.stderr)
        return 1
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
