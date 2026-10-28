#pragma once

#include "core/ProjectLayout.h"
#include "core/Result.h"

#include <array>
#include <filesystem>
#include <string_view>

namespace black::core {

/// The game shows its menus from game/language/strings/MainUS.bin. Each menu language is a replacement bank under
/// local/language-banks/<language>/MainUS.bin (made by scripts/localization); the retail English bank is kept
/// there as en-US the first time another language is installed.
inline constexpr std::string_view kRetailEnglishBankSha256 =
    "B23EEB4AD86A72894B40E7239D0EA8842E5A6BC05936A936401B608070E8C7BB";

/// Big.bin, Small.bin from the supported retail disc. Tests may supply hashes of their synthetic fonts.
using EnglishFontHashes = std::array<std::string_view, 2>;
inline constexpr EnglishFontHashes kRetailEnglishFontSha256 = {
    "119EDE7EC211DDEBDE76B46E4DCBEA86E486F1B13549FC1FE58ECB5ED59611C5",
    "F194F96F9389BA7F4CB126AC02E2F619F27DF0339AB97DF6FE7A740E5A1FCA35"};

[[nodiscard]] bool isMachineDraft(std::string_view language);
[[nodiscard]] std::filesystem::path languageBankPath(const ProjectLayout& project, std::string_view language);
[[nodiscard]] std::filesystem::path languageFontPath(const ProjectLayout& project, std::string_view language,
                                                     std::string_view fontName);

/// Keeps the game's retail English bank as en-US, once (true when it was kept now).
[[nodiscard]] Result<bool> preserveEnglishBank(const ProjectLayout& project,
                                               std::string_view retailEnglishSha256 = kRetailEnglishBankSha256);

/// Keeps the game's retail English fonts (Big.bin, Small.bin) as en-US, once.
[[nodiscard]] Result<bool> preserveEnglishFonts(const ProjectLayout& project,
                                                const EnglishFontHashes& retailHashes = kRetailEnglishFontSha256);

/// Installs the language's custom fonts if available, or restores retail English fonts.
[[nodiscard]] Result<bool> installLanguageFonts(const ProjectLayout& project, std::string_view language,
                                                const EnglishFontHashes& retailHashes = kRetailEnglishFontSha256);

/// Puts the language's bank in the game, after keeping the retail English one. Nothing changes when it is already
/// there.
[[nodiscard]] Result<bool> installLanguageBank(const ProjectLayout& project, std::string_view language,
                                               std::string_view retailEnglishSha256 = kRetailEnglishBankSha256,
                                               const EnglishFontHashes& retailFontHashes = kRetailEnglishFontSha256);

}  // namespace black::core
