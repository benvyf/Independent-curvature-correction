clear all; clc; close all;

%% =========================================================================
%  融合版：260330 基础 + Ang_deg 欧拉加权
%
%  相比260330的改进：
%    1. 全11点线性回归定 d0（保留）
%    2. 实测时加入 Ang_deg：用欧拉公式加权求 D
%       散光眼 theta≠0 时，D 与散光量解耦，不被短轴污染
%       标准球或无散光时 theta=0，退化为原均值通道，与260330完全一致
%
%  标定函数：calibrate_new   （与260330完全相同，无需改动）
%  实测函数：measure_eye_new （新增 Ang_deg 参数）
% =========================================================================

%% 1. 参数准备
PIXEL_SIZE = 0.0075;   % mm/pixel
D_array =  [-1.0; -0.9; -0.8; -0.7; -0.6; -0.5; -0.4; -0.3; -0.2; -0.1; 0.0; 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8; 0.9; 1.0];

%% 2. 读取图像并提取基础数据
targetFolder_6668 = 'D:\公司\项目\验光仪\角膜曲率\灵觉靶环2\曲率校正-20260401-140827\6.668';
targetFolder_7943 = 'D:\公司\项目\验光仪\角膜曲率\灵觉靶环2\曲率校正-20260401-140827\7.943';
targetFolder_932  = 'D:\公司\项目\验光仪\角膜曲率\灵觉靶环2\曲率校正-20260401-140827\9.32';
num_images = size(D_array,1);
fprintf('正在处理图像数据...\n');
[distPx_6668, L_in_6668, S_in_6668, Ang_in_6668, L_out_6668, S_out_6668, Ang_out_6668] = ProcessCorneaImages(targetFolder_6668,num_images);
[distPx_7943, L_in_7943, S_in_7943, Ang_in_7943, L_out_7943, S_out_7943, Ang_out_7943] = ProcessCorneaImages(targetFolder_7943,num_images);
[distPx_932,  L_in_932,  S_in_932,  Ang_in_932,  L_out_932,  S_out_932,  Ang_out_932 ] = ProcessCorneaImages(targetFolder_932,num_images);

%% 3. 标定（内环）
disp('>>> 开始标定（内环）...');
cal_in_6668 = calibrate_new(6.668, distPx_6668, L_in_6668, S_in_6668, D_array, PIXEL_SIZE);
cal_in_7943 = calibrate_new(7.943, distPx_7943, L_in_7943, S_in_7943, D_array, PIXEL_SIZE);
cal_in_9320 = calibrate_new(9.320, distPx_932,  L_in_932,  S_in_932,  D_array, PIXEL_SIZE);
cal_matrix_in = [cal_in_6668, cal_in_7943, cal_in_9320];

%% 4. 标定（外环）
disp('>>> 开始标定（外环）...');
cal_out_6668 = calibrate_new(6.668, distPx_6668, L_out_6668, S_out_6668, D_array, PIXEL_SIZE);
cal_out_7943 = calibrate_new(7.943, distPx_7943, L_out_7943, S_out_7943, D_array, PIXEL_SIZE);
cal_out_9320 = calibrate_new(9.320, distPx_932,  L_out_932,  S_out_932,  D_array, PIXEL_SIZE);
cal_matrix_out = [cal_out_6668, cal_out_7943, cal_out_9320];

disp('>>> 标定完成！');

%% 5. 全量验证（内环）
fprintf('\n=================================================================\n');
disp('【内环全量验证】');
batch_report_new(6.668, L_in_6668, S_in_6668, Ang_in_6668, distPx_6668, cal_matrix_in, PIXEL_SIZE, D_array);
batch_report_new(7.943, L_in_7943, S_in_7943, Ang_in_7943, distPx_7943, cal_matrix_in, PIXEL_SIZE, D_array);
batch_report_new(9.320, L_in_932,  S_in_932,  Ang_in_932,  distPx_932,  cal_matrix_in, PIXEL_SIZE, D_array);

%% 6. 全量验证（外环）
fprintf('\n=================================================================\n');
disp('【外环全量验证】');
batch_report_new(6.668, L_out_6668, S_out_6668, Ang_out_6668, distPx_6668, cal_matrix_out, PIXEL_SIZE, D_array);
batch_report_new(7.943, L_out_7943, S_out_7943, Ang_out_7943, distPx_7943, cal_matrix_out, PIXEL_SIZE, D_array);
batch_report_new(9.320, L_out_932,  S_out_932,  Ang_out_932,  distPx_932,  cal_matrix_out, PIXEL_SIZE, D_array);

%% 7. 单帧实测示例
disp('>>> 模拟单帧实测...');
test_idx  = 2;                        % D = -0.8 mm 对应索引
test_L_px = L_in_7943(test_idx);
test_S_px = S_in_7943(test_idx);
test_Ang  = Ang_in_7943(test_idx);
test_dist = distPx_7943(test_idx);   % 与上方统一用同一索引

% test_L_px = 94.458/2;
% test_S_px = 92.154/2;
% test_Ang = 0.951;
% test_dist = 109.247;

[R_L, R_S, D_shift] = measure_eye_new(test_L_px, test_S_px, test_Ang, test_dist, cal_matrix_in, PIXEL_SIZE);

fprintf('\n【单帧输出报告】\n');
fprintf('反算位移 D : %.4f mm\n', D_shift);
fprintf('长轴曲率 R1: %.4f mm  (%.2f D)\n', R_L, (1.3375-1)/R_L*1000);
fprintf('短轴曲率 R2: %.4f mm  (%.2f D)\n', R_S, (1.3375-1)/R_S*1000);
fprintf('散光量     : %.4f mm  (%.2f D)\n', abs(R_L-R_S), abs((1.3375-1)/R_L-(1.3375-1)/R_S)*1000);

% =========================================================================
%  核心函数 1：标定函数（与260330完全相同）
%
%  设计原则：
%    - 全11点线性回归定 d0_L、d0_S、f0（噪声降低 sqrt(11) 倍）
%    - 分轴独立 Cf_L=d0_L/f0、Cf_S=d0_S/f0（消除长短轴不对称）
%    - 分轴独立光学常数 C_L=d0_L/R、C_S=d0_S/R
%    - 以 f0 为插值键
% =========================================================================
function cal = calibrate_new(R_true, distPx, L_px, S_px, D_array, PIXEL_SIZE)
    dL_mm = 2 .* L_px .* PIXEL_SIZE;
    dS_mm = 2 .* S_px .* PIXEL_SIZE;
    f_mm  = distPx   .* PIXEL_SIZE;

    % 全点线性回归
    pL = polyfit(D_array, dL_mm, 1);
    pS = polyfit(D_array, dS_mm, 1);
    pf = polyfit(D_array, f_mm,  1);

    d0_L = pL(2);   k_L = pL(1);
    d0_S = pS(2);   k_S = pS(1);
    f0   = pf(2);

    % 分轴独立比值常数
    Cf_L = d0_L / f0;
    Cf_S = d0_S / f0;

    % 分轴独立光学常数
    C_L = d0_L / R_true;
    C_S = d0_S / R_true;

    % 写入标定结构体
    cal.R     = R_true;
    cal.f0    = f0;
    cal.d0_L  = d0_L;
    cal.d0_S  = d0_S;
    cal.k_L   = k_L;
    cal.k_S   = k_S;
    cal.Cf_L  = Cf_L;
    cal.Cf_S  = Cf_S;
    cal.C_L   = C_L;
    cal.C_S   = C_S;
    cal.R_L_ref = d0_L / C_L;
    cal.R_S_ref = d0_S / C_S;

    fprintf('  标定 R=%.3f mm: f0=%.5f, d0_L=%.5f, d0_S=%.5f, k_L=%.5f, k_S=%.5f, Cf_L=%.5f, Cf_S=%.5f\n', ...
        R_true, f0, d0_L, d0_S, k_L, k_S, Cf_L, Cf_S);
end


% =========================================================================
%  核心函数 2：实测函数
%
%  五步流程：
%    步骤1  像素 → mm
%    步骤2  f → 插值所有标定参数
%    步骤3  f → 分轴 d0 估算（d0_L=Cf_L*f, d0_S=Cf_S*f）
%    步骤4  欧拉加权求 D
%           theta=0（无散光/标准球）→ 均值通道，与260330完全一致
%           theta≠0（散光眼）→ 欧拉加权，D与散光量解耦
%    步骤5  分轴独立补偿输出 R_L、R_S
%
%  参数说明：
%    Ang_deg  散光轴位角（度）。标准球传0即可。
%             |d_L - d_S| < 0.005 mm 时自动判为无散光，theta 强制为 0。
% =========================================================================
function [R_L, R_S, D] = measure_eye_new(L_px, S_px, Ang_deg, distPx, cal_matrix, PIXEL_SIZE)
    % 步骤1：像素转mm
    dL_mm = 2 * L_px  * PIXEL_SIZE;
    dS_mm = 2 * S_px  * PIXEL_SIZE;
    f_mm  = distPx    * PIXEL_SIZE;

    % 步骤2：以 f_mm 为插值轴，从标定矩阵插出所有参数
    f0_vec = [cal_matrix.f0];

    Cf_L = interp1(f0_vec, [cal_matrix.Cf_L], f_mm, 'linear', 'extrap');
    Cf_S = interp1(f0_vec, [cal_matrix.Cf_S], f_mm, 'linear', 'extrap');
    k_L  = interp1(f0_vec, [cal_matrix.k_L],  f_mm, 'linear', 'extrap');
    k_S  = interp1(f0_vec, [cal_matrix.k_S],  f_mm, 'linear', 'extrap');
    C_L  = interp1(f0_vec, [cal_matrix.C_L],  f_mm, 'linear', 'extrap');
    C_S  = interp1(f0_vec, [cal_matrix.C_S],  f_mm, 'linear', 'extrap');

    % 步骤3：f → 分轴 d0 估算
    d0_L_est = Cf_L * f_mm;
    d0_S_est = Cf_S * f_mm;

    % 步骤4：欧拉加权求 D
    % |d_L - d_S| < 0.005 mm（约1.3像素）视为无散光，theta 强制为 0
    if abs(dL_mm - dS_mm) < 0.005
        theta = 0;
    else
        theta = deg2rad(Ang_deg);
    end

    if theta == 0
        % 无散光 / 标准球：均值通道（与260330完全一致）
        d_spot  = (dL_mm    + dS_mm)    / 2;
        d0_spot = (d0_L_est + d0_S_est) / 2;
        k_spot  = (k_L + k_S) / 2;
    else
        % 散光眼：欧拉公式加权
        % 1/d_spot = cos?θ/d_L + sin?θ/d_S
        c2 = cos(theta)^2;
        s2 = sin(theta)^2;
        d_spot  = 1 / (c2/dL_mm    + s2/dS_mm);
        d0_spot = 1 / (c2/d0_L_est + s2/d0_S_est);
        k_spot  = c2 * k_L + s2 * k_S;
    end

    D = (d_spot - d0_spot) / k_spot;

    % 步骤5：分轴独立补偿
    R_L = (dL_mm - k_L * D) / C_L;
    R_S = (dS_mm - k_S * D) / C_S;
end


% =========================================================================
%  批量验证函数
% =========================================================================
function batch_report_new(R_target, L_px, S_px, Ang_deg, distPx, cal_matrix, PIXEL_SIZE, D_array)
    fprintf('\n>>> 目标标准眼: R = %.3f mm <<<\n', R_target);
    fprintf('理论位移(mm) | 算出位移(mm) | 长轴R1    | 误差_长  | 短轴R2    | 误差_短  | 散光差\n');
    fprintf('%s\n', repmat('-', 1, 84));

    max_err_L = 0;
    max_err_S = 0;

    for i = 1:length(D_array)
        [R_L, R_S, D_calc] = measure_eye_new( ...
            L_px(i), S_px(i), Ang_deg(i), distPx(i), cal_matrix, PIXEL_SIZE);

        err_L = R_L - R_target;
        err_S = R_S - R_target;
        astig = abs(R_L - R_S);

        fprintf('  %6.1f   |    %8.4f  | %8.4f  | %+7.4f  | %8.4f  | %+7.4f  | %7.4f\n', ...
            D_array(i), D_calc, R_L, err_L, R_S, err_S, astig);

        if abs(err_L) > abs(max_err_L), max_err_L = err_L; end
        if abs(err_S) > abs(max_err_S), max_err_S = err_S; end
    end

    fprintf('最大误差                                   %+7.4f mm            %+7.4f mm\n', ...
        max_err_L, max_err_S);
end

% =========================================================================
%  图像处理函数（与原始代码相同，保持不变）
% =========================================================================
function [Dist_centroids, L_inner, S_inner, Ang_inner, L_outer, S_outer, Ang_outer] = ProcessCorneaImages(imageDir,num_images)
    Dist_centroids = zeros(num_images,1);
    L_inner = zeros(num_images,1); S_inner = zeros(num_images,1); Ang_inner = zeros(num_images,1);
    L_outer = zeros(num_images,1); S_outer = zeros(num_images,1); Ang_outer = zeros(num_images,1);

    for i = 1:num_images
        fileName = [num2str(i-1), '.png'];
        fullPath = fullfile(imageDir, fileName);
        if ~isfile(fullPath), continue; end

        img = imread(fullPath);
        if size(img,3)==3, img_gray = rgb2gray(img); else, img_gray = img; end
        Img_norm = im2double(img_gray);

        [Img_mark, level] = maskCircleFromCenter(Img_norm, 130);
        imgBW = Img_mark < level;

        se = strel('disk',3);
        mask_body = imopen(imgBW, se);
        mask_whole_disk = imfill(mask_body,'holes');
        Img_mark2 = mask_whole_disk .* Img_mark;
        imgBW2 = Img_mark2 > graythresh(Img_mark2(Img_mark2>0));

        [distPx, mask1, mask2] = twoLargestCCDistance(imgBW2, 8, 'centroid');
        Dist_centroids(i,1) = distPx;
        

        Light_Spot = mask1 + mask2;
        Circular_Spot = imgBW2 - Light_Spot;
        Circular_Spot(Circular_Spot < 0) = 0;

        se2 = strel('disk',2);
        imgProcessed = imopen(Circular_Spot, se2);

        CC = bwconncomp(imgProcessed);
        stats = regionprops(CC,'Area','Centroid','Eccentricity');
        valid_idx = find([stats.Area]>15 & [stats.Area]<200 & [stats.Eccentricity]<0.8);
        if length(valid_idx) < 6, continue; end

        centroids = vertcat(stats(valid_idx).Centroid);
        x = centroids(:,1); y = centroids(:,2);

        try
            global_ellipse = fit_ellipse_direct(x, y);
            centerX = global_ellipse.X0; centerY = global_ellipse.Y0;
        catch
            centerX = mean(x); centerY = mean(y);
        end

        dists = hypot(x-centerX, y-centerY);
        dists_norm = (dists-min(dists)) / (max(dists)-min(dists));
        level_dist = graythresh(dists_norm);
        split_radius = min(dists) + level_dist*(max(dists)-min(dists));

        innerIdx = dists <= split_radius; outerIdx = dists > split_radius;
        XY_inner = [x(innerIdx), y(innerIdx)];
        XY_outer = [x(outerIdx), y(outerIdx)];

        if sum(innerIdx) >= 6
            e = fit_ellipse_direct(XY_inner(:,1), XY_inner(:,2));
            L_inner(i,1) = e.a; S_inner(i,1) = e.b;
            Ang_inner(i,1) = mod(rad2deg(e.phi), 180);
        end
        if sum(outerIdx) >= 6
            e = fit_ellipse_direct(XY_outer(:,1), XY_outer(:,2));
            L_outer(i,1) = e.a; S_outer(i,1) = e.b;
            Ang_outer(i,1) = mod(rad2deg(e.phi), 180);
        end
    end
end

function [I_out, level] = maskCircleFromCenter(I_in, r)
    [H, W, C] = size(I_in);
    cx = (W+1)/2; cy = (H+1)/2;
    [X, Y] = meshgrid(1:W, 1:H);
    mask = (X-cx).^2 + (Y-cy).^2 <= r^2;
    I_out = I_in;
    for k = 1:C
        tmp = I_out(:,:,k); tmp(~mask) = 1; I_out(:,:,k) = tmp;
    end
    if C > 1, I_gray = rgb2grayLocal(I_in); else, I_gray = I_in; end
    roiPixels = I_gray(mask); roiPixels = im2double(roiPixels);
    if isempty(roiPixels) || std(roiPixels) < eps
        level = 0;
    else
        level = graythresh(roiPixels);
    end
end

function G = rgb2grayLocal(I)
    if size(I,3) == 3
        I = im2double(I);
        G = 0.2989*I(:,:,1) + 0.5870*I(:,:,2) + 0.1140*I(:,:,3);
    else
        G = I;
    end
end

function [distPx, mask1, mask2] = twoLargestCCDistance(BW, conn, mode)
    if nargin < 2 || isempty(conn), conn = 8; end
    if nargin < 3 || isempty(mode), mode = 'min'; end
    BW = logical(BW); [H, W] = size(BW);
    centerRow=241; centerCol=320; halfH=8; halfW=130;
    r1=max(1,centerRow-halfH); r2=min(H,centerRow+halfH);
    c1=max(1,centerCol-halfW); c2=min(W,centerCol+halfW);
    BWroi = BW(r1:r2, c1:c2);
    CC = bwconncomp(BWroi, conn); n = CC.NumObjects;
    distPx = NaN; mask1 = false(H,W); mask2 = false(H,W);
   
    if n < 2, return; end

    % 用质心的 x 坐标判断左右
    S = regionprops(CC, 'Centroid');
    centroids_x = arrayfun(@(s) s.Centroid(1), S);  % 各连通域质心的列坐标

    [~, idLeft]  = min(centroids_x);   % 最左边的连通域
    [~, idRight] = max(centroids_x);   % 最右边的连通域

    % 若只有两个且左右相同则无意义（正常情况不会发生）
    if idLeft == idRight, return; end

    m1_roi = false(size(BWroi)); m1_roi(CC.PixelIdxList{idLeft})  = true;
    m2_roi = false(size(BWroi)); m2_roi(CC.PixelIdxList{idRight}) = true;
    mask1(r1:r2,c1:c2) = m1_roi;
    mask2(r1:r2,c1:c2) = m2_roi;

    switch lower(mode)
        case 'min'
            D_bw = bwdist(mask1);
            distPx = min(D_bw(mask2));
        case 'centroid'
            c1_roi = S(idLeft).Centroid;
            c2_roi = S(idRight).Centroid;
            c1_full = [c1_roi(1)+(c1-1), c1_roi(2)+(r1-1)];
            c2_full = [c2_roi(1)+(c1-1), c2_roi(2)+(r1-1)];
            distPx = hypot(c1_full(1)-c2_full(1), c1_full(2)-c2_full(2));
    end
end

function ellipse_t = fit_ellipse_direct(x, y)
    x = x(:); y = y(:);
    if length(x) < 6, error('At least 6 points required'); end
    mean_x=mean(x); mean_y=mean(y);
    sx=(max(x)-min(x))/2; sy=(max(y)-min(y))/2;
    smax=max(sx,sy); if smax==0, smax=1; end
    xx=(x-mean_x)/smax; yy=(y-mean_y)/smax;
    D_mat=[xx.^2, xx.*yy, yy.^2, xx, yy, ones(size(xx))];
    S_mat=D_mat'*D_mat;
    C_mat=zeros(6,6); C_mat(1,3)=2; C_mat(3,1)=2; C_mat(2,2)=-1;
    [gevec,~]=eig(S_mat,C_mat); gevec=real(gevec);
    cond_vals=4*gevec(1,:).*gevec(3,:)-gevec(2,:).^2;
    pos_idx=find(cond_vals>1e-10);
    if isempty(pos_idx), error('Fit failed: Non-ellipse'); end
    [~,max_idx]=max(cond_vals(pos_idx));
    v=gevec(:,pos_idx(max_idx)); v=v/sqrt(cond_vals(pos_idx(max_idx)));
    A=v(1)/smax^2; B=v(2)/smax^2; Cv=v(3)/smax^2;
    Dc=v(4)/smax-2*A*mean_x-B*mean_y;
    Ec=v(5)/smax-2*Cv*mean_y-B*mean_x;
    Fc=v(6)+A*mean_x^2+B*mean_x*mean_y+Cv*mean_y^2-v(4)*mean_x/smax-v(5)*mean_y/smax;
    delta=B^2-4*A*Cv;
    X0=(2*Cv*Dc-B*Ec)/delta; Y0=(2*A*Ec-B*Dc)/delta;
    Fp=A*X0^2+B*X0*Y0+Cv*Y0^2+Dc*X0+Ec*Y0+Fc;
    Ap=-A/Fp; Bp=-B/Fp; Cp=-Cv/Fp;
    trM=Ap+Cp; detM=Ap*Cp-(Bp/2)^2;
    if detM<=0, error('Fit failed'); end
    diffM=sqrt((Ap-Cp)^2+Bp^2);
    l1=(trM+diffM)/2; l2=(trM-diffM)/2;
    if l1<=0||l2<=0, error('Fit failed'); end
    ellipse_t.X0=X0; ellipse_t.Y0=Y0;
    ellipse_t.a=1/sqrt(l2); ellipse_t.b=1/sqrt(l1);
    ellipse_t.phi=atan2(l2-Ap, Bp/2);
end
