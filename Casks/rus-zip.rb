cask "rus-zip" do
  version "1.0.0"

  if Hardware::CPU.arm?
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.0/RusZip-mac-arm64.zip"
    sha256 "<SHA256_MAC_ARM64_ZIP>"
  else
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.0/RusZip-mac-x64.zip"
    sha256 "<SHA256_MAC_X64_ZIP>"
  end

  name "RUS ZIP"
  desc "Modern cross-platform archive utility powered by Tar+Zstandard (.zrus) and Avalonia"
  homepage "https://github.com/utarn/rus-zip"

  app "RusZip.app"
  binary "#{appdir}/RusZip.app/Contents/MacOS/RusZip", target: "ruszip"

  zap trash: [
    "~/.config/rus-zip",
    "~/Library/Application Support/RUS ZIP",
    "~/Library/Preferences/com.ruszip.desktop.plist"
  ]
end
