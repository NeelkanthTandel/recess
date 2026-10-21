# Common tasks. Run `make help` for a list.

.PHONY: help build test lint format app install run-dev clean

help:
	@echo "make build     Debug build"
	@echo "make test      Run unit tests"
	@echo "make lint      Check formatting (swift format lint)"
	@echo "make format    Fix formatting in place"
	@echo "make app       Release build -> build/Recess.app"
	@echo "make install   Build, copy to ~/Applications and launch"
	@echo "make run-dev   Run a debug build against ./.devdata (your real data is untouched)"
	@echo "make clean     Remove build products"

build:
	swift build

test:
	swift test

lint:
	swift format lint --strict --recursive --configuration .swift-format Sources Tests Package.swift

format:
	swift format format --in-place --recursive --configuration .swift-format Sources Tests Package.swift

app:
	scripts/build_app.sh

install:
	scripts/build_app.sh --install

run-dev: build
	mkdir -p .devdata
	RECESS_DATA_DIR="$(CURDIR)/.devdata" "$$(swift build --show-bin-path)/Recess"

clean:
	rm -rf .build build .devdata
