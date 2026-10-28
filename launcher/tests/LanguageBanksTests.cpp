#include "TempFolder.h"
#include "core/Files.h"
#include "core/LanguageBanks.h"
#include "core/Sha256.h"

#include <doctest/doctest.h>

#ifdef _WIN32
#include <windows.h>
#endif

using namespace black::core;

namespace {

std::string text(const std::filesystem::path& path) {
    const auto bytes = readFile(path);
    return bytes ? std::string(bytes.value().begin(), bytes.value().end()) : std::string("<missing>");
}

std::string hashOf(std::string_view data) {
    Sha256 hash;
    hash.update(std::as_bytes(std::span(data)));
    return Sha256::toHex(hash.finish());
}

struct LanguageFixture {
    black::tests::TempFolder folder;
    ProjectLayout project{folder.path()};
    const std::string bigHash = hashOf("english_big_font");
    const std::string smallHash = hashOf("english_small_font");
    const EnglishFontHashes fontHashes{bigHash, smallHash};
    const std::string bankHash = hashOf("english_bank");

    LanguageFixture() {
        folder.write("game/language/strings/MainUS.bin", "english_bank");
        folder.write("local/language-banks/pt-BR/MainUS.bin", "portugues_bank");
        folder.write("game/language/fonts/Big.bin", "english_big_font");
        folder.write("game/language/fonts/Small.bin", "english_small_font");
        folder.write("translations/pt-BR/fonts/Big.bin", "ptbr_big_font");
        folder.write("translations/pt-BR/fonts/Small.bin", "ptbr_small_font");
    }

    void checkEnglish() const {
        CHECK(text(folder.path() / "game/language/strings/MainUS.bin") == "english_bank");
        CHECK(text(folder.path() / "game/language/fonts/Big.bin") == "english_big_font");
        CHECK(text(folder.path() / "game/language/fonts/Small.bin") == "english_small_font");
    }

    Result<bool> install(std::string_view language) const {
        return installLanguageBank(project, language, bankHash, fontHashes);
    }
};

}  // namespace

TEST_CASE("Changing the menu language keeps the retail English bank first") {
    const black::tests::TempFolder folder;
    const ProjectLayout project(folder.path());
    folder.write("game/language/strings/MainUS.bin", "english");
    folder.write("local/language-banks/pt-BR/MainUS.bin", "portugues");
    const std::string retail = hashOf("english");
    const auto game = folder.path() / "game/language/strings/MainUS.bin";

    REQUIRE(installLanguageBank(project, "pt-BR", retail).ok());
    CHECK(text(game) == "portugues");
    CHECK(text(languageBankPath(project, "en-US")) == "english");

    REQUIRE(installLanguageBank(project, "en-US", retail).ok());
    CHECK(text(game) == "english");

    CHECK_FALSE(installLanguageBank(project, "fr", retail).ok());  // no French bank
    CHECK_FALSE(installLanguageBank(project, "xx", retail).ok());
}

TEST_CASE("An unrecognised game bank is never kept as English") {
    const black::tests::TempFolder folder;
    const ProjectLayout project(folder.path());
    folder.write("game/language/strings/MainUS.bin", "modified");
    folder.write("local/language-banks/pt-BR/MainUS.bin", "portugues");
    const auto installed = installLanguageBank(project, "pt-BR", hashOf("english"));
    REQUIRE_FALSE(installed.ok());
    CHECK(installed.error().message.find("retail English") != std::string::npos);
    CHECK(text(folder.path() / "game/language/strings/MainUS.bin") == "modified");
}

TEST_CASE("Language fonts are swapped for pt-BR and restored for other languages") {
    const LanguageFixture fixture;
    REQUIRE(fixture.install("pt-BR").ok());
    CHECK(text(fixture.folder.path() / "game/language/fonts/Big.bin") == "ptbr_big_font");
    CHECK(text(fixture.folder.path() / "game/language/fonts/Small.bin") == "ptbr_small_font");
    CHECK(text(fixture.folder.path() / "game/language/strings/MainUS.bin") == "portugues_bank");
    REQUIRE(fixture.install("pt-BR").ok());  // A repeated launch preserves the verified English backups.
    REQUIRE(fixture.install("en-US").ok());
    fixture.checkEnglish();
    REQUIRE(fixture.install("pt-BR").ok());
    fixture.folder.write("local/language-banks/fr/MainUS.bin", "french_bank");
    REQUIRE(fixture.install("fr").ok());
    CHECK(text(fixture.folder.path() / "game/language/fonts/Big.bin") == "english_big_font");
    CHECK(text(fixture.folder.path() / "game/language/fonts/Small.bin") == "english_small_font");
    CHECK(text(fixture.folder.path() / "game/language/strings/MainUS.bin") == "french_bank");
}

TEST_CASE("Already patched fonts are never labelled as retail English") {
    const LanguageFixture fixture;
    fixture.folder.write("game/language/fonts/Small.bin", "ptbr_small_font");
    const auto installed = fixture.install("pt-BR");
    REQUIRE_FALSE(installed.ok());
    CHECK(installed.error().message.find("retail English font Small.bin") != std::string::npos);
    CHECK_FALSE(std::filesystem::exists(fixture.folder.path() / "local/language-banks/en-US/fonts/Big.bin"));
    CHECK_FALSE(std::filesystem::exists(fixture.folder.path() / "local/language-banks/en-US/fonts/Small.bin"));
    CHECK(text(fixture.folder.path() / "game/language/fonts/Big.bin") == "english_big_font");
    CHECK(text(fixture.folder.path() / "game/language/fonts/Small.bin") == "ptbr_small_font");
    CHECK(text(fixture.folder.path() / "game/language/strings/MainUS.bin") == "english_bank");
}

TEST_CASE("Font recovery selects verified retail bytes rather than the first backup name") {
    const LanguageFixture fixture;
    fixture.folder.write("game/language/fonts/Big.bin", "ptbr_big_font");
    fixture.folder.write("game/language/fonts/Small.bin", "ptbr_small_font");
    fixture.folder.write("game/language/fonts/backups/Big-0.bin", "modified_big_font");
    fixture.folder.write("game/language/fonts/backups/Small-0.bin", "modified_small_font");
    fixture.folder.write("game/language/fonts/backups/Big-1.bin", "english_big_font");
    fixture.folder.write("game/language/fonts/backups/Small-1.bin", "english_small_font");
    fixture.folder.write("local/language-banks/en-US/fonts/Big.bin", "previously_poisoned_backup");
    REQUIRE(fixture.install("en-US").ok());
    fixture.checkEnglish();
    CHECK(text(fixture.folder.path() / "local/language-banks/en-US/fonts/Big.bin") == "english_big_font");
    CHECK(text(fixture.folder.path() / "local/language-banks/en-US/fonts/Small.bin") == "english_small_font");
}

TEST_CASE("An existing unverified English font backup is rejected") {
    const LanguageFixture fixture;
    fixture.folder.write("game/language/fonts/Big.bin", "ptbr_big_font");
    fixture.folder.write("local/language-banks/en-US/fonts/Big.bin", "ptbr_big_font");
    REQUIRE_FALSE(fixture.install("en-US").ok());
    CHECK(text(fixture.folder.path() / "game/language/fonts/Big.bin") == "ptbr_big_font");
    CHECK(text(fixture.folder.path() / "game/language/strings/MainUS.bin") == "english_bank");
}

TEST_CASE("A missing language bank leaves active fonts and strings unchanged") {
    const LanguageFixture fixture;
    std::filesystem::remove(fixture.folder.path() / "local/language-banks/pt-BR/MainUS.bin");
    REQUIRE_FALSE(fixture.install("pt-BR").ok());
    fixture.checkEnglish();
    CHECK_FALSE(std::filesystem::exists(fixture.folder.path() / "local/language-banks/en-US/fonts/Big.bin"));
}

TEST_CASE("A modified English string bank cannot trigger a font change") {
    const LanguageFixture fixture;
    fixture.folder.write("game/language/strings/MainUS.bin", "unrecognised_bank");
    REQUIRE_FALSE(fixture.install("pt-BR").ok());
    CHECK(text(fixture.folder.path() / "game/language/strings/MainUS.bin") == "unrecognised_bank");
    CHECK(text(fixture.folder.path() / "game/language/fonts/Big.bin") == "english_big_font");
    CHECK(text(fixture.folder.path() / "game/language/fonts/Small.bin") == "english_small_font");
}

TEST_CASE("A staging failure leaves all active language files unchanged") {
    const LanguageFixture fixture;
    fixture.folder.write("game/language/fonts/Small.bin.launcher-tmp", "leftover_from_an_interrupted_change");
    REQUIRE_FALSE(fixture.install("pt-BR").ok());
    fixture.checkEnglish();
    CHECK(text(fixture.folder.path() / "game/language/fonts/Small.bin.launcher-tmp") ==
          "leftover_from_an_interrupted_change");
    CHECK_FALSE(std::filesystem::exists(fixture.folder.path() / "game/language/fonts/Big.bin.launcher-tmp"));
    CHECK_FALSE(std::filesystem::exists(fixture.folder.path() / "game/language/fonts/Big.bin.launcher-rollback"));
}

#ifdef _WIN32
TEST_CASE("A failed string-bank replacement rolls back both installed fonts") {
    const LanguageFixture fixture;
    const auto bank = fixture.folder.path() / "game/language/strings/MainUS.bin";
    const HANDLE locked = CreateFileW(bank.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                                      OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    REQUIRE(locked != INVALID_HANDLE_VALUE);
    const auto installed = fixture.install("pt-BR");
    CloseHandle(locked);
    REQUIRE_FALSE(installed.ok());
    CHECK(installed.error().message.find("Could not write") != std::string::npos);
    fixture.checkEnglish();
    for (const auto* name : {"Big.bin", "Small.bin"}) {
        CHECK_FALSE(std::filesystem::exists(fixture.folder.path() / "game/language/fonts" /
                                            (std::string(name) + ".launcher-rollback")));
    }
}
#endif
