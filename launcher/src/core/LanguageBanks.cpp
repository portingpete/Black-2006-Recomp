#include "core/LanguageBanks.h"

#include "core/Settings.h"
#include "core/Sha256.h"
#include "core/Text.h"

#include <algorithm>
#include <vector>

namespace black::core {
namespace fs = std::filesystem;
namespace {

constexpr std::array<std::string_view, 2> kFontNames = {"Big.bin", "Small.bin"};

fs::path gameBankPath(const ProjectLayout& project) {
    return project.gameDirectory() / "language" / "strings" / "MainUS.bin";
}

fs::path gameFontPath(const ProjectLayout& project, std::string_view fontName) {
    return project.gameDirectory() / "language" / "fonts" / fontName;
}

fs::path preservedEnglishFontPath(const ProjectLayout& project, std::string_view fontName) {
    return project.localDirectory() / "language-banks" / "en-US" / "fonts" / fontName;
}

std::string hashOf(const fs::path& path) {
    const auto digest = sha256File(path);
    return digest ? Sha256::toHex(digest.value()) : std::string();
}

/// Copies beside the target and renames over it, so the game never sees half a bank.
Result<bool> replaceFile(const fs::path& source, const fs::path& target, bool overwrite) {
    std::error_code error;
    const fs::path temporary = target.string() + ".launcher-tmp";
    fs::copy_file(source, temporary, fs::copy_options::overwrite_existing, error);
    if (!error) {
        if (!overwrite && fs::exists(target, error)) {
            fs::remove(temporary, error);
            return false;
        }
        fs::rename(temporary, target, error);
    }
    if (error) {
        fs::remove(temporary, error);
        return Error{"Could not write " + toUtf8(target)};
    }
    return true;
}

struct FileReplacement {
    fs::path source;
    fs::path target;
};

/// Stage the entire language change before replacing anything. Retain rollback copies until all renames succeed.
Result<bool> replaceFiles(const std::vector<FileReplacement>& replacements) {
    struct StagedFile {
        fs::path target;
        fs::path temporary;
        fs::path previous;
        bool temporaryCreated = false;
        bool previousCreated = false;
        bool installed = false;
        bool keepPrevious = false;
    };
    std::vector<StagedFile> staged;
    const auto cleanup = [&] {
        std::error_code ignored;
        for (const auto& file : staged) {
            if (file.temporaryCreated) {
                fs::remove(file.temporary, ignored);
            }
            if (file.previousCreated && !file.keepPrevious) {
                fs::remove(file.previous, ignored);
            }
        }
    };
    for (const auto& replacement : replacements) {
        const std::string sourceHash = hashOf(replacement.source);
        if (sourceHash.empty()) {
            cleanup();
            return Error{"Could not read " + toUtf8(replacement.source)};
        }
        if (sourceHash == hashOf(replacement.target)) {
            continue;
        }
        staged.push_back({replacement.target, replacement.target.string() + ".launcher-tmp",
                          replacement.target.string() + ".launcher-rollback"});
        auto& file = staged.back();
        std::error_code error;
        const bool temporaryExists = fs::exists(file.temporary, error);
        if (error || temporaryExists) {
            cleanup();
            return Error{"Could not stage the language change: " + toUtf8(file.temporary)};
        }
        const bool previousExists = fs::exists(file.previous, error);
        if (error || previousExists) {
            cleanup();
            return Error{"A previous language change needs recovery: " + toUtf8(file.previous)};
        }
        file.previousCreated = true;
        fs::copy_file(file.target, file.previous, fs::copy_options::none, error);
        if (!error) {
            file.temporaryCreated = true;
            fs::copy_file(replacement.source, file.temporary, fs::copy_options::none, error);
        }
        if (error) {
            cleanup();
            return Error{"Could not stage the language change for " + toUtf8(file.target)};
        }
    }
    for (auto& file : staged) {
        std::error_code error;
        fs::rename(file.temporary, file.target, error);
        if (error) {
            std::string message = "Could not write " + toUtf8(file.target);
            for (auto it = staged.rbegin(); it != staged.rend(); ++it) {
                if (!it->installed) {
                    continue;
                }
                std::error_code restoreError;
                fs::rename(it->previous, it->target, restoreError);
                if (restoreError) {
                    it->keepPrevious = true;
                    message += ". Restore the original from " + toUtf8(it->previous);
                } else {
                    it->previousCreated = false;
                }
            }
            cleanup();
            return Error{message};
        }
        file.installed = true;
        file.temporaryCreated = false;
    }
    cleanup();
    return true;
}

Result<std::vector<FileReplacement>> englishFontSources(const ProjectLayout& project,
                                                        const EnglishFontHashes& retailHashes) {
    std::vector<FileReplacement> sources;
    for (std::size_t i = 0; i < kFontNames.size(); ++i) {
        const auto fontName = kFontNames[i];
        const fs::path english = preservedEnglishFontPath(project, fontName);
        const fs::path game = gameFontPath(project, fontName);
        std::error_code error;
        if (!fs::is_regular_file(game, error)) {
            continue;
        }
        if (hashOf(english) == retailHashes[i]) {
            continue;
        }
        fs::path candidate;
        if (hashOf(game) == retailHashes[i]) {
            candidate = game;
        } else {
            const fs::path backupDir = game.parent_path() / "backups";
            const std::string stem = fs::path(fontName).stem().string();
            fs::directory_iterator entry(backupDir, error);
            const fs::directory_iterator end;
            while (!error && entry != end) {
                if (entry->is_regular_file(error) && entry->path().filename().string().rfind(stem, 0) == 0 &&
                    hashOf(entry->path()) == retailHashes[i]) {
                    candidate = entry->path();
                    break;
                }
                entry.increment(error);
            }
        }
        if (candidate.empty()) {
            return Error{"The retail English font " + std::string(fontName) +
                         " is missing or unrecognised. Restore it from your disc at " + toUtf8(english) +
                         " before changing the menu language."};
        }
        sources.push_back({candidate, english});
    }
    return sources;
}

std::vector<FileReplacement> languageFontReplacements(const ProjectLayout& project, std::string_view language) {
    std::vector<FileReplacement> replacements;
    for (const auto fontName : kFontNames) {
        std::error_code error;
        const fs::path target = gameFontPath(project, fontName);
        if (!fs::is_regular_file(target, error)) {
            continue;
        }
        const fs::path custom = languageFontPath(project, language, fontName);
        replacements.push_back(
            {fs::is_regular_file(custom, error) ? custom : preservedEnglishFontPath(project, fontName), target});
    }
    return replacements;
}

}  // namespace

bool isMachineDraft(std::string_view language) {
    return language == "es" || language == "fr" || language == "de" || language == "it";
}

fs::path languageBankPath(const ProjectLayout& project, std::string_view language) {
    return project.localDirectory() / "language-banks" / fs::path(language) / "MainUS.bin";
}

Result<bool> preserveEnglishBank(const ProjectLayout& project, std::string_view retailEnglishSha256) {
    const fs::path english = languageBankPath(project, "en-US");
    const fs::path game = gameBankPath(project);
    std::error_code error;
    if (fs::is_regular_file(english, error)) {
        return false;
    }
    if (!fs::is_regular_file(game, error)) {
        return Error{"The game's menu text (game/language/strings/MainUS.bin) is missing."};
    }
    if (hashOf(game) != retailEnglishSha256) {
        return Error{"The game's MainUS.bin is not the retail English one. Put the original back (from your disc) at " +
                     toUtf8(english) + " before changing the menu language."};
    }
    fs::create_directories(english.parent_path(), error);
    return replaceFile(game, english, false);
}

fs::path languageFontPath(const ProjectLayout& project, std::string_view language, std::string_view fontName) {
    const fs::path translationFont = project.root() / "translations" / fs::path(language) / "fonts" / fontName;
    std::error_code error;
    if (fs::is_regular_file(translationFont, error)) {
        return translationFont;
    }
    return project.localDirectory() / "language-banks" / fs::path(language) / "fonts" / fontName;
}

Result<bool> preserveEnglishFonts(const ProjectLayout& project, const EnglishFontHashes& retailHashes) {
    const auto sources = englishFontSources(project, retailHashes);
    if (!sources) {
        return sources.error();
    }
    for (const auto& source : sources.value()) {
        std::error_code error;
        fs::create_directories(source.target.parent_path(), error);
        auto res = replaceFile(source.source, source.target, true);
        if (!res) {
            return res;
        }
    }
    return !sources.value().empty();
}

Result<bool> installLanguageFonts(const ProjectLayout& project, std::string_view language,
                                  const EnglishFontHashes& retailHashes) {
    const auto preserved = preserveEnglishFonts(project, retailHashes);
    if (!preserved) {
        return preserved;
    }
    return replaceFiles(languageFontReplacements(project, language));
}

Result<bool> installLanguageBank(const ProjectLayout& project, std::string_view language,
                                 std::string_view retailEnglishSha256, const EnglishFontHashes& retailFontHashes) {
    if (std::find(choices::kLanguages.begin(), choices::kLanguages.end(), language) == choices::kLanguages.end()) {
        return Error{"Unknown menu language " + std::string(language)};
    }
    const fs::path target = gameBankPath(project);
    const fs::path source = languageBankPath(project, language);
    std::error_code error;
    if (!fs::is_regular_file(target, error)) {
        return Error{"The game's menu text (game/language/strings/MainUS.bin) is missing."};
    }
    if (language != "en-US" && !fs::is_regular_file(source, error)) {
        return Error{"The " + std::string(language) + " menu text is missing: " + toUtf8(source)};
    }
    const auto preserved = preserveEnglishBank(project, retailEnglishSha256);
    if (!preserved) {
        return preserved;
    }
    const auto fontResult = preserveEnglishFonts(project, retailFontHashes);
    if (!fontResult) {
        return fontResult;
    }
    auto replacements = languageFontReplacements(project, language);
    replacements.push_back({source, target});
    return replaceFiles(replacements);
}

}  // namespace black::core
