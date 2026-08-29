cask "rus-zip" do
  version "1.0.4"

  if Hardware::CPU.arm?
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/RusZip-mac-arm64.zip"
    sha256 "44c59cd3d469c068af175d1751f2e92940dfebb788003659d95f5258348b5cc5"
  else
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/RusZip-mac-x64.zip"
    sha256 "75cde22e7079cc97786ca095fdaa171b15aa236f5fb84e1c57bdf6de9bf5771e"
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
