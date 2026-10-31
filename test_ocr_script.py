    parser.add_argument("--max-tokens", type=int, default=4096, help="Giới hạn token đầu ra mỗi trang")
    parser.add_argument("--format", choices=("png", "jpeg"), default="png", help="Định dạng ảnh gửi API")
    parser.add_argument("--prompt", default=DEFAULT_PROMPT, help="Prompt OCR tùy chỉnh")
    parser.add_argument("--overwrite", action="store_true", help="Cho phép ghi đè file đầu ra đã tồn tại")
    args = parser.parse_args()

    pdf_path = Path(args.pdf).expanduser().resolve()
    if not pdf_path.is_file():
        print(f"LỖI: Không tìm thấy file PDF: {pdf_path}", file=sys.stderr)
        return 2
    if pdf_path.suffix.lower() != ".pdf":
        print("LỖI: File đầu vào phải có đuôi .pdf", file=sys.stderr)
        return 2
    if args.dpi < 72 or args.dpi > 400:
        print("LỖI: --dpi nên nằm trong khoảng 72–400.", file=sys.stderr)
        return 2
    if args.start_page < 1:
        print("LỖI: --start-page phải >= 1.", file=sys.stderr)
        return 2

    output_path = (
        Path(args.output).expanduser().resolve()
        if args.output
        else pdf_path.with_name(f"{pdf_path.stem}.ocr.txt")
    )
    if output_path.exists() and not args.overwrite:
        print(
            f"LỖI: File đầu ra đã tồn tại: {output_path}\n"
            "Dùng --overwrite để ghi đè.",
            file=sys.stderr,
        )
        return 2

    session = requests.Session()
    session.headers.update({"User-Agent": "local-pdf-ocr/1.0"})

    try:
        print(f"API: {args.base_url}")
        health_url = f"{args.base_url.rstrip('/')}/health"
        try:
            health = session.get(health_url, timeout=min(args.timeout, 10))
            print(f"Health HTTP: {health.status_code}")
        except requests.RequestException as exc:
            print(
                f"CẢNH BÁO: Không kiểm tra được {health_url}: {exc}\n"
                "Tiếp tục thử /v1/models...",
                file=sys.stderr,
            )

        model_id = get_model_id(session, args.base_url, min(args.timeout, 30))
        print(f"Model: {model_id}")

        with fitz.open(pdf_path) as document:
            total_pages = len(document)
            if total_pages == 0:
                raise RuntimeError("PDF không có trang nào.")
            end_page = args.end_page or total_pages
            if args.start_page > total_pages:
                raise RuntimeError(
                    f"--start-page={args.start_page} vượt quá tổng số {total_pages} trang."
                )
            if end_page < args.start_page:
                raise RuntimeError("--end-page không được nhỏ hơn --start-page.")
            if end_page > total_pages:
                raise RuntimeError(
                    f"--end-page={end_page} vượt quá tổng số {total_pages} trang."
                )

            results: list[str] = []
            failed_pages: list[tuple[int, str]] = []
            start_time = time.perf_counter()

            for page_number in range(args.start_page, end_page + 1):
                page = document.load_page(page_number - 1)
                print(
                    f"[{page_number}/{end_page}] Render trang và chạy OCR...",
                    flush=True,
                )
                try:
                    data_url = render_page_to_data_url(page, args.dpi, args.format)
                    text = ocr_page(
                        session=session,
                        base_url=args.base_url,
                        model_id=model_id,
                        data_url=data_url,
                        prompt=args.prompt,
                        timeout=args.timeout,
                        max_tokens=args.max_tokens,
                    )
                    results.append(f"===== TRANG {page_number} =====\n{text}\n")
                    print(f"    Hoàn tất ({len(text)} ký tự)")
                except Exception as exc:
                    message = str(exc)
                    failed_pages.append((page_number, message))
                    results.append(
                        f"===== TRANG {page_number} =====\n"
                        f"[OCR THẤT BẠI: {message}]\n"
                    )
                    print(f"    LỖI: {message}", file=sys.stderr)

            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text("\n".join(results), encoding="utf-8-sig")
            elapsed = time.perf_counter() - start_time

            print("\n========== KẾT QUẢ ==========")
            print(f"PDF: {pdf_path}")
            print(f"Số trang xử lý: {end_page - args.start_page + 1}")
            print(f"Số trang lỗi: {len(failed_pages)}")
            print(f"Thời gian: {elapsed:.1f} giây")
            print(f"File kết quả: {output_path}")

            if failed_pages:
                print("\nCác trang lỗi:")
                for page_number, message in failed_pages:
                    print(f"- Trang {page_number}: {message}")
                return 1

    except requests.RequestException as exc:
        print(
            f"LỖI kết nối API: {exc}\n"
            "Kiểm tra server, cổng, endpoint và model đã load.",
            file=sys.stderr,
        )
        return 1
    except (RuntimeError, fitz.FileDataError, OSError) as exc:
        print(f"LỖI: {exc}", file=sys.stderr)
        return 1
    finally:
