---
id: EXAMPLE
title: <TODO: một câu, mô tả KẾT QUẢ chứ không mô tả việc phải làm>
scope: src/**
out_of_scope: tests/**, migrations/**
---

## Bối cảnh

<TODO: Vì sao việc này tồn tại. Người dùng nào đang gặp vấn đề gì.
2–4 câu. Agent cần cái này để chọn đúng cách sửa, không chỉ để sửa được.>

## Mục tiêu

<TODO: Trạng thái kết thúc mong muốn. Một đoạn.
Viết theo kiểu "sau khi xong, X sẽ làm được Y", không viết "sửa hàm Z".>

## Tiêu chí chấp nhận

> Dùng Given/When/Then. Mỗi dòng phải **kiểm chứng được bằng máy** — nếu bạn không
> chỉ ra được verifier nào chứng minh nó, thì nó chưa phải tiêu chí, mới chỉ là mong muốn.
>
> Đây là phần quan trọng nhất của file. Nhiệm vụ thất bại phần lớn vì phần này mơ hồ,
> chứ không phải vì agent kém.

- [ ] Given <TODO: tiền đề>, When <TODO: hành động>, Then <TODO: kết quả quan sát được>
- [ ] Given <TODO:>, When <TODO:>, Then <TODO:>
- [ ] <TODO: trường hợp biên — cái này agent luôn quên nếu bạn không viết ra>
- [ ] <TODO: trường hợp lỗi — chuyện gì xảy ra khi đầu vào sai?>

## Ngoài phạm vi

<TODO: Liệt kê rõ. Không viết ra thì agent sẽ "tiện tay" refactor và bạn sẽ mất
buổi chiều để review một diff gấp năm lần cần thiết.>

- Không đụng <TODO:>
- Không refactor <TODO:>

## Gợi ý (không bắt buộc)

<TODO: Nếu bạn đã biết chỗ cần sửa, ghi ra — tiết kiệm cả một vòng tìm kiếm.
Nếu chưa biết, để trống; đừng đoán bừa, đoán sai còn tệ hơn không nói gì.>

## Rủi ro đã biết

<TODO: Chỗ nào dễ hỏng ngầm? Có hành vi nào phụ thuộc code này mà không có test?>
