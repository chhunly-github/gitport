# Bundled runtimes

## Editor fonts

JetBrains Mono (JetBrains), Fira Code (Nikita Prokopov), and Source Code Pro (Adobe) are bundled as unmodified regular TTF faces under the SIL Open Font License 1.1. Their complete license notices ship inside the JAR at `fonts/`. Sources: https://github.com/JetBrains/JetBrainsMono , https://github.com/tonsky/FiraCode/releases/tag/6.2 , https://github.com/adobe-fonts/source-code-pro . Fonts are loaded only into GitPort's Java process; no Windows font installation is performed. Consolas and Cascadia Code are offered when available from the host and are not redistributed.

GitPort invokes a complete Git for Windows runtime bundled in `git/`, not a host installation. Its Git and component license notices are preserved in that directory, including `git/LICENSE.txt`, `git/usr/share/licenses/`, and notices elsewhere in the distribution. Git is GPL version 2; component licenses vary. Official project and matching release source: https://github.com/git-for-windows/git . Review redistribution obligations before sharing the binary distribution beyond your own use. This offline build uses the installed Git for Windows runtime as its source and excludes host temporary files, uninstaller and machine-level `etc/gitconfig`.

The Java runtime is generated from the chosen JDK. The current build was generated from Oracle JDK 21.0.12 already installed on this computer. Runtime legal notices are included under `runtime/legal`. Review that vendor's terms before redistributing this packaged runtime. To use another vendor, rebuild with an appropriate Java 21+ OpenJDK distribution. No Java or Git installation is required on the target computer.
