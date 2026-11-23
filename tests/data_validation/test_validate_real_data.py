import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZipFile

from scripts import validate_real_data


CLUBS = "Enciclopedia_Futsal_Equipas_Masculino1.xlsx"
COACHES = "Enciclopedia_Futsal_Entrenadores_Masculino1.xlsx"
PLAYERS = "Enciclopedia_Futsal_Jogadores_Activos_Masculino{}.xlsx"


def workbook(path, sheet, rows):
    namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    content = []
    for row in rows:
        cells = "".join(f'<c t="inlineStr"><is><t xml:space="preserve">{escape(str(item))}</t></is></c>'
                        for item in row)
        content.append(f"<row>{cells}</row>")
    with ZipFile(path, "w") as result:
        result.writestr("[Content_Types].xml",
                        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                        '</Types>')
        result.writestr("xl/workbook.xml",
                        f'<workbook xmlns="{namespace}" xmlns:r="{relationship}"><sheets>'
                        f'<sheet name="{sheet}" sheetId="1" r:id="rId1"/></sheets></workbook>')
        result.writestr("xl/_rels/workbook.xml.rels",
                        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                        f'<Relationship Id="rId1" Type="{relationship}/worksheet" Target="/xl/worksheets/sheet1.xml"/>'
                        '</Relationships>')
        result.writestr("xl/worksheets/sheet1.xml",
                        f'<worksheet xmlns="{namespace}"><sheetData>{"".join(content)}</sheetData></worksheet>')


def run(folder, review=None, extra=()):
    output, errors = io.StringIO(), io.StringIO()
    args = [str(folder)]
    if review is not None:
        args += ["--reviewed-links", str(review)]
    args.extend(extra)
    with contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
        status = validate_real_data.main(args)
    return status, output.getvalue(), errors.getvalue()


class ValidateRealDataTests(unittest.TestCase):
    def test_reports_candidate_categories_per_id_without_changing_inputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            workbook(folder / CLUBS, "Equipas",
                     [("ID", "Nombre"), (10, "Atlético Norte"), (20, "Same Club"), (30, "Same Club")])
            workbook(folder / COACHES, "Treinadores",
                     [("ID", "Equipo Actual"), (1, "atlético norte"), (2, ""), (3, "Same Club")])
            workbook(folder / PLAYERS.format(1), "Jogadores",
                     [("ID", "Equipo Actual", "Nombre"), (1, " ATLÉTICO  NORTE ", "Hidden Player"),
                      (2, "Same Club", "Hidden Player"), (3, "Not in catalog", "Hidden Player"),
                      (4, "", "Hidden Player"), (5, "Same Club", "Hidden Player"),
                      ("", "Atlético Norte", "Hidden Player"), ("not-id", "", "Hidden Player")])
            workbook(folder / PLAYERS.format(2), "Jogadores",
                     [("ID", "Equipo Actual"), (1, "Atlético Norte"), (5, "Atlético Norte")])
            before = {f.name: f.read_bytes() for f in folder.iterdir()}

            status, output, errors = run(folder)

            self.assertEqual(0, status)
            self.assertEqual("", errors)
            report = json.loads(output)
            self.assertEqual("unifutsal.real-data-validation/v1", report["schema"])
            self.assertEqual({"rows": 9, "unique_ids": 5, "duplicate_id_rows": 2,
                              "missing_id_rows": 1, "invalid_id_rows": 1}, report["players"]["records"])
            self.assertEqual({"one_club_candidate": 1, "ambiguous_club": 1, "unmatched_club": 1,
                              "no_club": 1, "conflicting_club_labels": 1}, report["players"]["memberships"])
            self.assertEqual({"one_club_candidate": 1, "ambiguous_club": 1, "unmatched_club": 0,
                              "no_club": 1, "conflicting_club_labels": 0}, report["coaches"]["memberships"])
            self.assertEqual(3, report["clubs"]["records"]["unique_ids"])
            self.assertEqual(1, report["clubs"]["ambiguous_name_keys"])
            for value in ["Hidden Player", "Atlético Norte", "Not in catalog", "Same Club"]:
                self.assertNotIn(value, output)
            self.assertEqual(before, {f.name: f.read_bytes() for f in folder.iterdir()})

    def test_rejects_missing_catalog_without_producing_partial_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            status, output, errors = run(Path(tmp))
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("Faltan los Excel", errors)

    def test_rejects_a_workbook_without_the_required_columns(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            workbook(folder / CLUBS, "Equipas", [("ID", "unexpected"), (1, "Hidden Club")])
            workbook(folder / COACHES, "Treinadores", [("ID", "Equipo Actual")])
            workbook(folder / PLAYERS.format(1), "Jogadores", [("ID", "Equipo Actual")])

            status, output, errors = run(folder)

        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("columnas requeridas", errors)
        self.assertNotIn("Hidden Club", errors)


class ReviewedLinksTests(unittest.TestCase):
    def make_catalog(self, folder):
        workbook(folder / CLUBS, "Equipas",
                 [("ID", "Nombre"), (10, "Club Norte"), (20, "Same Club"), (30, "Same Club")])
        workbook(folder / COACHES, "Treinadores",
                 [("ID", "Equipo Actual"), (1, "Variant club")])
        workbook(folder / PLAYERS.format(1), "Jogadores",
                 [("ID", "Equipo Actual"), (1, "Same Club"), (2, "Same Club"),
                  (3, "Club Norte"), (4, "Same Club"), (4, "Another label")])

    def test_review_resolves_only_explicit_people_and_keeps_each_file_unchanged(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            self.make_catalog(folder)
            review = folder / "review.csv"
            review.write_text("kind,person_id,team_label,club_id\n"
                              "player,1, Same  CLub ,20\n"
                              "coach,1,Variant club,10\n", encoding="utf-8-sig")
            before = {file.name: file.read_bytes() for file in folder.iterdir()}

            status, output, errors = run(folder, review)

            self.assertEqual((0, ""), (status, errors))
            report = json.loads(output)
            self.assertEqual({"one_club_candidate": 1, "ambiguous_club": 1, "unmatched_club": 0,
                              "no_club": 0, "conflicting_club_labels": 1, "manually_reviewed_club": 1},
                             report["players"]["memberships"])
            self.assertEqual(1, report["coaches"]["memberships"]["manually_reviewed_club"])
            self.assertNotIn("Variant club", output)
            self.assertNotIn("Same Club", output)
            self.assertEqual(before, {file.name: file.read_bytes() for file in folder.iterdir()})
            self.assertNotIn("manually_reviewed_club", json.loads(run(folder)[1])["players"]["memberships"])

    def test_rejects_malformed_or_unverifiable_review_files_without_partial_output(self):
        cases = [
            ("member,1,Same Club,20\n", "tipo de persona"),
            ("player,bad,Same Club,20\n", "ID no válido"),
            ("player,1,Same Club,999\n", "club no disponible"),
            ("player,99,Same Club,20\n", "persona no encontrada"),
            ("player,1,Old private label,20\n", "nombre de equipo no coincide"),
            ("player,4,Same Club,20\n", "nombres de equipo contradictorios"),
            ("player,1,Same Club,10\n", "club no está entre los candidatos"),
            ("player,1,Same Club,20\nplayer,1,Same Club,30\n", "decisión duplicada"),
            ("player,1,Same Club,20,extra\n", "cuatro campos"),
        ]
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            self.make_catalog(folder)
            review = folder / "review.csv"
            for contents, error in cases:
                with self.subTest(error=error):
                    review.write_text("kind,person_id,team_label,club_id\n" + contents, encoding="utf-8")
                    before = review.read_bytes()
                    status, output, errors = run(folder, review)
                    self.assertEqual(1, status)
                    self.assertEqual("", output)
                    self.assertIn(error, errors)
                    self.assertNotIn("Old private label", errors)
                    self.assertEqual(before, review.read_bytes())


class CandidateLookupTests(unittest.TestCase):
    def make_catalog(self, folder):
        workbook(folder / CLUBS, "Equipas",
                 [("ID", "Nombre", "País", "Ciudad"), (30, "Same Club", "Spain", "Madrid"),
                  (20, "Same Club", "Portugal", "Porto"), (10, "Club Norte", "Spain", "")])
        workbook(folder / COACHES, "Treinadores",
                 [("ID", "Equipo Actual", "Nombre"), (1, "Club Norte", "Hidden Coach")])
        workbook(folder / PLAYERS.format(1), "Jogadores",
                 [("ID", "Equipo Actual", "Nombre"), (1, "Same Club", "Hidden Player"),
                  (2, "", ""), (3, "Unmatched Label", ""), (4, "Same Club", ""), (4, "Club Norte", "")])

    def test_lookup_lists_all_matching_club_ids_without_disclosing_personal_fields(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            self.make_catalog(folder)
            before = {f.name: f.read_bytes() for f in folder.iterdir()}

            status, output, errors = run(folder, extra=["--lookup", "player", "1"])

            self.assertEqual((0, ""), (status, errors))
            report = json.loads(output)
            self.assertEqual("unifutsal.club-candidate-lookup/v1", report["schema"])
            self.assertEqual(("player", 1, "ambiguous_club"), (report["kind"], report["person_id"], report["status"]))
            self.assertEqual([{"team_label": "same club", "candidates": [
                {"club_id": 20, "entries": [{"name": "Same Club", "country": "Portugal", "city": "Porto"}]},
                {"club_id": 30, "entries": [{"name": "Same Club", "country": "Spain", "city": "Madrid"}]},
            ]}], report["team_labels"])
            self.assertNotIn("Hidden Player", output)
            self.assertEqual(before, {f.name: f.read_bytes() for f in folder.iterdir()})

    def test_lookup_preserves_conflicts_unknown_labels_and_missing_clubs(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            self.make_catalog(folder)
            for kind, person, state, labels in [
                ("player", "2", "no_club", []),
                ("player", "3", "unmatched_club", ["unmatched label"]),
                ("player", "4", "conflicting_club_labels", ["club norte", "same club"]),
                ("coach", "1", "one_club_candidate", ["club norte"]),
            ]:
                with self.subTest(state=state):
                    status, output, errors = run(folder, extra=["--lookup", kind, person])
                    self.assertEqual((0, ""), (status, errors))
                    result = json.loads(output)
                    self.assertEqual(state, result["status"])
                    self.assertEqual(labels, [item["team_label"] for item in result["team_labels"]])
                    if state == "unmatched_club":
                        self.assertEqual([], result["team_labels"][0]["candidates"])
                    if kind == "coach":
                        self.assertIsNone(result["team_labels"][0]["candidates"][0]["entries"][0]["city"])
                        self.assertNotIn("Hidden Coach", output)

    def test_lookup_rejects_an_invalid_or_absent_id_without_partial_output(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            self.make_catalog(folder)
            for kind, person, expected in [
                ("person", "1", "tipo de persona"),
                ("player", "0", "ID no válido"),
                ("player", "99", "persona no encontrada"),
            ]:
                with self.subTest(expected=expected):
                    status, output, errors = run(folder, extra=["--lookup", kind, person])
                    self.assertEqual((1, ""), (status, output))
                    self.assertIn(expected, errors)


if __name__ == "__main__":
    unittest.main()
