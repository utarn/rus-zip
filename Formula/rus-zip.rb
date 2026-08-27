class RusZip < Formula
  desc "Cross-platform compression suite powered by Tar+Zstandard (.zrus)"
  homepage "https://github.com/utarn/rus-zip"
  version "1.0.2"
  license "MIT"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/utarn/rus-zip/releases/download/v1.0.2/rus-zip-cli-osx-arm64.tar.gz"
      sha256 "7511d1a8e63fdc35479d2320405b604d15adee94231fd3f70bb2fe06bbe94e5c"
    else
      url "https://github.com/utarn/rus-zip/releases/download/v1.0.2/rus-zip-cli-osx-x64.tar.gz"
      sha256 "f60883cd9789d5276c44ffef6204b74b90505fbce0e3c11ad9996e7ba827b320"
    end
  end

  on_linux do
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.2/rus-zip-cli-linux-x64"
    sha256 "dcfc5c16c21202c7182bd87daa8fe35358e82bc3c23f3cd6c0bbf17cd124cbd1"
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
