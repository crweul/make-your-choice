# Makefile for Make Your Choice
# Defaults to ~/.local for user installation (no sudo required)
# Use PREFIX=/usr/local with sudo for system-wide installation

# Detect if running as root
ifeq ($(shell id -u),0)
    PREFIX ?= /usr/local
else
    PREFIX ?= $(HOME)/.local
endif

BINDIR ?= $(PREFIX)/bin
DATADIR ?= $(PREFIX)/share
APPLICATIONSDIR ?= $(DATADIR)/applications
ICONSDIR ?= $(DATADIR)/icons/hicolor
LICENSEDIR ?= $(DATADIR)/licenses/make-your-choice

BINARY_NAME = make-your-choice
DESKTOP_FILE = make-your-choice.desktop
ICON_FILE = icon.ico

# Cargo build profile (release or debug)
PROFILE ?= release
ifeq ($(PROFILE),release)
	CARGO_FLAGS = --release
	TARGET_DIR = target/release
else
	CARGO_FLAGS =
	TARGET_DIR = target/debug
endif

.PHONY: all build install uninstall clean help

all: build

# Build the application
build:
	@echo "Building $(BINARY_NAME) ($(PROFILE) mode)..."
	cargo build $(CARGO_FLAGS)
	@echo "Build complete: $(TARGET_DIR)/$(BINARY_NAME)"

# Install the application
install: build
	@echo "Installing $(BINARY_NAME) to $(PREFIX)..."
	# Install binary
	install -Dm755 "$(TARGET_DIR)/$(BINARY_NAME)" "$(DESTDIR)$(BINDIR)/$(BINARY_NAME)"
	# Create desktop file with absolute path
	@sed 's|Exec=make-your-choice|Exec=$(BINDIR)/$(BINARY_NAME)|g' "$(DESKTOP_FILE)" > "$(DESKTOP_FILE).tmp"
	install -Dm644 "$(DESKTOP_FILE).tmp" "$(DESTDIR)$(APPLICATIONSDIR)/$(DESKTOP_FILE)"
	@rm -f "$(DESKTOP_FILE).tmp"
	# Install icon (256x256)
	install -Dm644 "$(ICON_FILE)" "$(DESTDIR)$(ICONSDIR)/256x256/apps/$(BINARY_NAME).ico"
	# Install license
	install -Dm644 "../../LICENSE" "$(DESTDIR)$(LICENSEDIR)/LICENSE"
	# Update desktop database
	@if [ -z "$(DESTDIR)" ]; then \
		if command -v update-desktop-database >/dev/null 2>&1; then \
			echo "Updating desktop database..."; \
			update-desktop-database "$(APPLICATIONSDIR)" 2>/dev/null || true; \
		fi; \
	fi
	@echo ""
	@echo "Installation complete!"
	@echo "Binary installed to: $(DESTDIR)$(BINDIR)/$(BINARY_NAME)"
	@echo ""
	@if [ "$(PREFIX)" = "$(HOME)/.local" ]; then \
		echo "Note: Make sure $(HOME)/.local/bin is in your PATH"; \
		echo "Add this to your ~/.bashrc or ~/.zshrc if needed:"; \
		echo "  export PATH=\"\$$HOME/.local/bin:\$$PATH\""; \
		echo ""; \
	fi
	@echo "To run the application, type: $(BINARY_NAME)"
	@echo "Or find it in your application launcher as 'Make Your Choice'"

# Uninstall the application
uninstall:
	@echo "Uninstalling $(BINARY_NAME)..."
	rm -f "$(DESTDIR)$(BINDIR)/$(BINARY_NAME)"
	rm -f "$(DESTDIR)$(APPLICATIONSDIR)/$(DESKTOP_FILE)"
	rm -f "$(DESTDIR)$(ICONSDIR)/256x256/apps/$(BINARY_NAME).ico"
	rm -rf "$(DESTDIR)$(LICENSEDIR)"
	@echo "Uninstallation complete!"

# Clean build artifacts
clean:
	@echo "Cleaning build artifacts..."
	cargo clean
	@echo "Clean complete!"

# Show help
help:
	@echo "Make Your Choice - Makefile targets:"
	@echo ""
	@echo "  make              - Build the application (release mode)"
	@echo "  make build        - Build the application"
	@echo "  make install      - Install the application (user install to ~/.local)"
	@echo "  make uninstall    - Uninstall the application"
	@echo "  make clean        - Clean build artifacts"
	@echo "  make help         - Show this help message"
	@echo ""
	@echo "Variables:"
	@echo "  PREFIX=/path      - Installation prefix (default: ~/.local for users)"
	@echo "  DESTDIR=/path     - Staging directory for packaging"
	@echo "  PROFILE=debug     - Build in debug mode instead of release"
	@echo ""
	@echo "Examples:"
	@echo "  make install                         - Install to ~/.local (no sudo needed)"
	@echo "  sudo make install PREFIX=/usr/local  - Install system-wide to /usr/local"
	@echo "  sudo make install PREFIX=/usr        - Install system-wide to /usr"
	@echo "  make install DESTDIR=/tmp/staging    - Stage installation for packaging"
