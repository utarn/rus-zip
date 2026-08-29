class RusZip < Formula
  desc "Cross-platform compression suite powered by Tar+Zstandard (.zrus)"
  homepage "https://github.com/utarn/rus-zip"
  version "1.0.4"
  license "Proprietary"

  on_macos do
    # Apple Silicon only — Intel (osx-x64) builds are discontinued.
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/rus-zip-cli-osx-arm64.zip"
    sha256 "60fbda049c05ae3ecc977c165dd974f473dcb52a23c77c9d77a2b13fe6e09532"
  end

  on_linux do
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/rus-zip-cli-linux-x64"
    sha256 "2ec2e738b12b9c4ade69ee90ddf7dfd8e15062cf1c635a763c4c76ea6c4c3148"
  end

  def install
    if OS.mac?
      bin.install "rus-zip"
    else
      bin.install "rus-zip-cli-linux-x64" => "rus-zip"
    end
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/rus-zip --version")
  end
end
