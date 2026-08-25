class RusZip < Formula
  desc "Cross-platform compression suite powered by Tar+Zstandard (.zrus)"
  homepage "https://github.com/utarn/rus-zip"
  version "1.0.0"
  license "MIT"

  if Hardware::CPU.arm?
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.0/rus-zip-cli-osx-arm64.tar.gz"
    sha256 "<SHA256_CLI_OSX_ARM64>"
  else
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.0/rus-zip-cli-osx-x64.tar.gz"
    sha256 "<SHA256_CLI_OSX_X64>"
  end

  def install
    bin.install "rus-zip"
  end

  test do
    assert_match "rus-zip", shell_output("#{bin}/rus-zip --version")
  end
end
