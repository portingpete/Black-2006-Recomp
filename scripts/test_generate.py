"""Focused checks for BLACK's generated-code pipeline."""

from pathlib import Path
import json
from unittest.mock import patch

import generate


def test_disasm_forwards_feedback_seeds():
    with patch.object(generate, "command") as command:
        generate.run_disasm(
            Path("toolkit"), Path("game.xbe"), Path("analysis.json"),
            Path("disasm"), Path("icall-seeds.json"))

    _, module, arguments = command.call_args.args
    assert module == "tools.disasm", module
    seed_index = arguments.index("--seed-functions")
    assert arguments[seed_index + 1] == Path("icall-seeds.json"), arguments


def test_post_transform_audit_describes_final_sources():
    from tempfile import TemporaryDirectory

    with TemporaryDirectory() as temp:
        root = Path(temp)
        generated = root / "generated"
        output = root / "output"
        generated.mkdir()
        output.mkdir()
        source = (
            "void sub_00001000(void)\n{\n    /* TODO: cli */\n}\n"
            "/* cmp was a TODO */\n/* FAILED: sub_00002000 */\n"
            "void sub_00003000(void)\n{ RECOMP_UNRESOLVED_STUB_HIT(0x3000u); }\n")
        for directory in (generated, output):
            (directory / "recomp_0000.c").write_text(source, encoding="utf-8")
            (directory / "recomp_funcs.h").write_text(
                "#define RECOMP_UNRESOLVED_STUB_HIT(va) ((void)0)\n"
                "void sub_00001000(void);\n", encoding="utf-8")
        raw_summary = root / "summary.json"
        raw_summary.write_text('{"unimplemented":{"cli":[1,2]}}', encoding="utf-8")

        audit = generate.write_post_transform_audit(generated, output, raw_summary)

        assert audit["generated_function_definitions"] == 2
        assert audit["remaining_todo_comments"] == 1
        assert audit["failed_translation_markers"] == 1
        assert audit["unresolved_stub_trace_sites"] == 1
        assert len(audit["files"]) == 2
        assert len(audit["source_sha256"]) == 64
        summary = json.loads(raw_summary.read_text(encoding="utf-8"))
        assert summary["summary_stage"] == "raw_recompiler_before_black_transforms"
        assert summary["post_transform_audit"]["source_sha256"] == audit["source_sha256"]


if __name__ == "__main__":
    test_disasm_forwards_feedback_seeds()
    test_post_transform_audit_describes_final_sources()
    print("2 checks passed")
