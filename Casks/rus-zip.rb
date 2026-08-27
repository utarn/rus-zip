cask "rus-zip" do
  version "1.0.2"

  if Hardware::CPU.arm?
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.2/RusZip-mac-arm64.zip"
    sha256 "7996522b62f51221c65165266bca2bd9a078f0dfb0c7908ea7772d2523afd0ad"
  else
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.2/RusZip-mac-x64.zip"
    sha256 "5ec6eb030cfba3ff43da0ac29930389f9d13704ae08f0d169fa17f33539d78d3"
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
