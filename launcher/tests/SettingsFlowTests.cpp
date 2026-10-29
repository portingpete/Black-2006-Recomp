#include "TempFolder.h"
#include "app/SettingsFlow.h"
#include "app/Translations.h"
#include "core/Settings.h"

#include <doctest/doctest.h>

#include <algorithm>

namespace {

std::size_t row(const black::app::Page& page, std::string_view label) {
    const auto found =
        std::find_if(page.rows.begin(), page.rows.end(), [label](const auto& item) { return item.label == label; });
    REQUIRE(found != page.rows.end());
    return static_cast<std::size_t>(found - page.rows.begin());
}

TEST_CASE("Launcher graphics changes preserve settings changed during play") {
    const black::tests::TempFolder folder;
    folder.write("CMakeLists.txt", "\n");
    folder.write("scripts/generate.py", "\n");
    const auto project = black::core::ProjectLayout::locate(folder.path());
    REQUIRE(project.has_value());
    black::app::setLanguage("en-US");
    black::app::SettingsFlow flow(*project);
    flow.handle({row(flow.page(), "GRAPHICS"), 0});
    auto inGame = black::core::loadSettings(*project);
    inGame.crosshair = "circle";
    inGame.verticalFov = 90;
    REQUIRE(black::core::saveSettings(*project, inGame).ok());
    flow.handle({row(flow.page(), "ANISOTROPIC FILTERING"), -1});
    const auto saved = black::core::loadSettings(*project);
    CHECK(saved.anisotropy == 8);
    CHECK(saved.crosshair == "circle");
    CHECK(saved.verticalFov == 90);
}

}  // namespace

TEST_CASE("Shader precompilation is optional, persistent and passed to the next launch") {
    const black::tests::TempFolder folder;
    folder.write("CMakeLists.txt", "\n");
    folder.write("scripts/generate.py", "\n");
    const auto project = black::core::ProjectLayout::locate(folder.path());
    REQUIRE(project.has_value());
    black::app::setLanguage("en-US");
    black::app::SettingsFlow flow(*project);
    flow.handle({row(flow.page(), "GRAPHICS"), 0});
    const auto option = row(flow.page(), "PRECOMPILE SHADERS AT STARTUP");
    CHECK(flow.page().rows[option].value == "OFF");
    flow.handle({option, 1});
    CHECK(flow.page().rows[option].value == "ON");
    const auto saved = black::core::loadSettings(*project);
    CHECK(saved.precompileShaders);
    CHECK(black::core::settingsFromJson(black::core::settingsJson(saved)).precompileShaders);
    flow.handle({option, -1});
    CHECK_FALSE(black::core::loadSettings(*project).precompileShaders);
    black::app::setLanguage("pt-BR");
    CHECK(flow.page().rows[row(flow.page(), "PRÉ-COMPILAR SHADERS AO INICIAR")].value == "DESLIGADO");
    black::app::setLanguage("en-US");
}

TEST_CASE("Aim mode switches and persists without overwriting live video settings") {
    const black::tests::TempFolder folder;
    folder.write("CMakeLists.txt", "\n");
    folder.write("scripts/generate.py", "\n");
    const auto project = black::core::ProjectLayout::locate(folder.path());
    REQUIRE(project.has_value());
    black::app::setLanguage("en-US");
    black::app::SettingsFlow flow(*project);
    flow.handle({row(flow.page(), "GAMEPLAY"), 0});
    const auto aim = row(flow.page(), "AIM MODE");
    CHECK(flow.page().rows[aim].value == "TOGGLE");
    flow.handle({aim, 1});
    CHECK(flow.page().rows[aim].value == "HOLD");
    CHECK(black::core::loadSettings(*project).aimMode == "hold");
    auto settings = black::core::loadSettings(*project);
    CHECK(black::core::videoIni(settings).find("aim_mode=hold") != std::string::npos);
    CHECK(black::core::settingsFromJson(black::core::settingsJson(settings)).aimMode == "hold");
    CHECK(black::core::applyVideoIni(settings, "aim_mode=invalid\n") == 0);
    CHECK(settings.aimMode == "hold");
    flow.handle({aim, -1});
    CHECK(black::core::loadSettings(*project).aimMode == "toggle");
    black::app::setLanguage("pt-BR");
    CHECK(flow.page().rows[row(flow.page(), "MODO DE MIRA")].value == "ALTERNAR");
    black::app::setLanguage("en-US");
}

TEST_CASE("Subtitles can be disabled and enabled persistently in Gameplay") {
    const black::tests::TempFolder folder;
    folder.write("CMakeLists.txt", "\n");
    folder.write("scripts/generate.py", "\n");
    const auto project = black::core::ProjectLayout::locate(folder.path());
    REQUIRE(project.has_value());
    CHECK(black::core::settingsFromJson("{}").subtitles);
    black::app::setLanguage("en-US");
    black::app::SettingsFlow flow(*project);
    flow.handle({row(flow.page(), "GAMEPLAY"), 0});
    const auto subtitles = row(flow.page(), "SUBTITLES");
    CHECK(flow.page().rows[subtitles].value == "ON");
    flow.handle({subtitles, 1});
    CHECK(flow.page().rows[subtitles].value == "OFF");
    const auto saved = black::core::loadSettings(*project);
    CHECK_FALSE(saved.subtitles);
    CHECK_FALSE(black::core::settingsFromJson(black::core::settingsJson(saved)).subtitles);
    flow.handle({subtitles, -1});
    CHECK(black::core::loadSettings(*project).subtitles);
    black::app::setLanguage("pt-BR");
    CHECK(flow.page().rows[row(flow.page(), "LEGENDAS")].value == "LIGADO");
    black::app::setLanguage("en-US");
}

TEST_CASE("Sprint and jump are independent opt-ins in Gameplay") {
    const black::tests::TempFolder folder;
    const black::core::ProjectLayout project(folder.path());
    black::app::setLanguage("en-US");
    black::app::SettingsFlow flow(project);
    flow.handle({row(flow.page(), "GAMEPLAY"), 0});
    const auto sprint = row(flow.page(), "SPRINT"), jump = row(flow.page(), "JUMP");
    CHECK(flow.page().rows[sprint].value == "OFF");
    CHECK(flow.page().rows[jump].value == "OFF");
    flow.handle({sprint, 1});
    auto s = black::core::loadSettings(project);
    CHECK(s.sprint == "on");
    CHECK(s.jump == "off");
    flow.handle({jump, 1});
    flow.handle({sprint, 1});
    s = black::core::loadSettings(project);
    CHECK(s.sprint == "off");
    CHECK(s.jump == "on");
    const auto restored = black::core::settingsFromJson(black::core::settingsJson(s));
    CHECK(restored.sprint == "off");
    CHECK(restored.jump == "on");
    CHECK(black::core::applyVideoIni(s, "sprint=invalid\njump=1\n") == 0);
    CHECK(black::core::settingsFromJson(R"({"Sprint":true,"Jump":1})").sprint == "off");
    CHECK(black::core::settingsFromJson(R"({"Sprint":true,"Jump":1})").jump == "off");
    black::app::setLanguage("pt-BR");
    flow.reload();
    CHECK(flow.page().rows[sprint].label == "CORRER");
    CHECK(flow.page().rows[jump].label == "PULAR");
    black::app::setLanguage("en-US");
}

TEST_CASE("Gameplay settings persist independent opt-in choices") {
    const black::tests::TempFolder folder;
    folder.write("CMakeLists.txt", "\n");
    folder.write("scripts/generate.py", "\n");
    const auto project = black::core::ProjectLayout::locate(folder.path());
    REQUIRE(project.has_value());
    black::app::setLanguage("en-US");
    black::app::SettingsFlow flow(*project);
    auto page = flow.page();
    flow.handle({row(page, "DISPLAY"), 0});
    page = flow.page();
    CHECK(std::none_of(page.rows.begin(), page.rows.end(), [](const auto& item) {
        return item.label == "CROSSHAIR" || item.label == "HIT MARKERS" ||
               item.label == "OPTIONAL SECONDARY OBJECTIVES";
    }));
    flow.back();
    page = flow.page();
    flow.handle({row(page, "GAMEPLAY"), 0});
    page = flow.page();
    CHECK(page.title == "GAMEPLAY");
    const auto crosshair = row(page, "CROSSHAIR");
    const auto markers = row(page, "HIT MARKERS");
    const auto secondaries = row(page, "OPTIONAL SECONDARY OBJECTIVES");
    CHECK(page.rows[secondaries].value == "OFF");
    CHECK(page.rows[secondaries].help ==
          "Allow mission completion without the required secondary objectives. Primary objectives are still required.");
    CHECK(page.rows[crosshair].value == "OFF");
    CHECK(page.rows[markers].value == "OFF");
    flow.handle({secondaries, 1});
    CHECK(flow.page().rows[secondaries].value == "ON");
    flow.handle({markers, 1});
    CHECK(flow.page().rows[crosshair].value == "OFF");
    CHECK(flow.page().rows[markers].value == "ON");
    flow.handle({crosshair, 1});
    CHECK(flow.page().rows[crosshair].value == "CROSS");
    flow.handle({markers, 1});
    CHECK(flow.page().rows[markers].value == "OFF");
    const auto loaded = black::core::loadSettings(*project);
    CHECK(loaded.crosshair == "cross");
    CHECK(loaded.hitmarkers == "off");
    CHECK(loaded.optional_secondaries == "on");
    black::app::SettingsFlow reopened(*project);
    reopened.handle({row(reopened.page(), "GAMEPLAY"), 0});
    CHECK(reopened.page().rows[crosshair].value == "CROSS");
    CHECK(reopened.page().rows[markers].value == "OFF");
    CHECK(reopened.page().rows[secondaries].value == "ON");
    reopened.handle({secondaries, -1});
    CHECK(reopened.page().rows[secondaries].value == "OFF");
    CHECK(black::core::loadSettings(*project).optional_secondaries == "off");
    reopened.handle({crosshair, -1});
    CHECK(reopened.page().rows[crosshair].value == "OFF");
    auto changedInGame = black::core::loadSettings(*project);
    changedInGame.optional_secondaries = "on";
    REQUIRE(black::core::saveSettings(*project, changedInGame).ok());
    reopened.reload();
    CHECK(reopened.page().rows[secondaries].value == "ON");
}
