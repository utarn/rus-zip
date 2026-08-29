cask "rus-zip" do
  version "1.0.4"

  # Apple Silicon only — Intel (osx-x64) builds are discontinued.
  url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/RusZip-mac-arm64.zip"
  sha256 "d2c57a23bf7fdd2fe27f0762a067381b8a937f8d9d2f7e9b32efe81b0f536a1c"

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
