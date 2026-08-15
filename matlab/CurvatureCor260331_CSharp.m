clear; clc; close all;

% Compare the original MATLAB 260331 method with the current C# method.
% Both methods use the same extracted image data. The C# path reproduces:
%   1. positive-value filtering and one-pass robust linear fitting;
%   2. float storage of calibration nodes;
%   3. f0-sorted interpolation/extrapolation;
%   4. spherical branching by |dL-dS| < 0.005 mm;
%   5. two-pass Euler/axis-ratio fusion for non-spherical measurements.

PIXEL_SIZE_MATLAB = 0.0075;
PIXEL_SIZE_CSHARP = double(single(0.0075));
D_array = -1.0 + (0:20)' * (2.0 / 20.0);

scriptDir = fileparts(mfilename('fullpath'));
batchFolders = dir(fullfile(scriptDir, '*', '*-20260401-140827'));
batchFolders = batchFolders([batchFolders.isdir]);
if numel(batchFolders) ~= 1
    error('Expected exactly one *-20260401-140827 data folder below the script directory.');
end
dataRoot = fullfile(batchFolders(1).folder, batchFolders(1).name);
targetFolder_6668 = fullfile(dataRoot, '6.668');
targetFolder_7943 = fullfile(dataRoot, '7.943');
targetFolder_9320 = fullfile(dataRoot, '9.32');

num_images = numel(D_array);
fprintf('Extracting image data...\n');
[dist_6668, Lin_6668, Sin_6668, Ain_6668, Lout_6668, Sout_6668, Aout_6668] = ...
    ProcessCorneaImages(targetFolder_6668, num_images);
[dist_7943, Lin_7943, Sin_7943, Ain_7943, Lout_7943, Sout_7943, Aout_7943] = ...
    ProcessCorneaImages(targetFolder_7943, num_images);
[dist_9320, Lin_9320, Sin_9320, Ain_9320, Lout_9320, Sout_9320, Aout_9320] = ...
    ProcessCorneaImages(targetFolder_9320, num_images);

fprintf('Building MATLAB 260331 and C# calibration matrices...\n');

mat_in = [ ...
    calibrate_matlab260331(6.668, dist_6668, Lin_6668, Sin_6668, D_array, PIXEL_SIZE_MATLAB), ...
    calibrate_matlab260331(7.943, dist_7943, Lin_7943, Sin_7943, D_array, PIXEL_SIZE_MATLAB), ...
    calibrate_matlab260331(9.320, dist_9320, Lin_9320, Sin_9320, D_array, PIXEL_SIZE_MATLAB)];

mat_out = [ ...
    calibrate_matlab260331(6.668, dist_6668, Lout_6668, Sout_6668, D_array, PIXEL_SIZE_MATLAB), ...
    calibrate_matlab260331(7.943, dist_7943, Lout_7943, Sout_7943, D_array, PIXEL_SIZE_MATLAB), ...
    calibrate_matlab260331(9.320, dist_9320, Lout_9320, Sout_9320, D_array, PIXEL_SIZE_MATLAB)];

cs_in = [ ...
    calibrate_csharp260331(6.668, dist_6668, Lin_6668, Sin_6668, D_array, PIXEL_SIZE_CSHARP), ...
    calibrate_csharp260331(7.943, dist_7943, Lin_7943, Sin_7943, D_array, PIXEL_SIZE_CSHARP), ...
    calibrate_csharp260331(9.320, dist_9320, Lin_9320, Sin_9320, D_array, PIXEL_SIZE_CSHARP)];

cs_out = [ ...
    calibrate_csharp260331(6.668, dist_6668, Lout_6668, Sout_6668, D_array, PIXEL_SIZE_CSHARP), ...
    calibrate_csharp260331(7.943, dist_7943, Lout_7943, Sout_7943, D_array, PIXEL_SIZE_CSHARP), ...
    calibrate_csharp260331(9.320, dist_9320, Lout_9320, Sout_9320, D_array, PIXEL_SIZE_CSHARP)];

fprintf('\n================ INNER RING ================\n');
batch_compare(6.668, Lin_6668, Sin_6668, Ain_6668, dist_6668, mat_in, cs_in, ...
    PIXEL_SIZE_MATLAB, PIXEL_SIZE_CSHARP, D_array);
batch_compare(7.943, Lin_7943, Sin_7943, Ain_7943, dist_7943, mat_in, cs_in, ...
    PIXEL_SIZE_MATLAB, PIXEL_SIZE_CSHARP, D_array);
batch_compare(9.320, Lin_9320, Sin_9320, Ain_9320, dist_9320, mat_in, cs_in, ...
    PIXEL_SIZE_MATLAB, PIXEL_SIZE_CSHARP, D_array);

fprintf('\n================ OUTER RING ================\n');
batch_compare(6.668, Lout_6668, Sout_6668, Aout_6668, dist_6668, mat_out, cs_out, ...
    PIXEL_SIZE_MATLAB, PIXEL_SIZE_CSHARP, D_array);
batch_compare(7.943, Lout_7943, Sout_7943, Aout_7943, dist_7943, mat_out, cs_out, ...
    PIXEL_SIZE_MATLAB, PIXEL_SIZE_CSHARP, D_array);
batch_compare(9.320, Lout_9320, Sout_9320, Aout_9320, dist_9320, mat_out, cs_out, ...
    PIXEL_SIZE_MATLAB, PIXEL_SIZE_CSHARP, D_array);

% Same single-frame example as CurvatureCor260331.m.
test_idx = 2;
[mRL, mRS, mD] = measure_matlab260331( ...
    Lin_7943(test_idx), Sin_7943(test_idx), Ain_7943(test_idx), ...
    dist_7943(test_idx), mat_in, PIXEL_SIZE_MATLAB);
[cRL, cRS, cD, cDetail] = measure_csharp260331( ...
    Lin_7943(test_idx), Sin_7943(test_idx), Ain_7943(test_idx), ...
    dist_7943(test_idx), cs_in, PIXEL_SIZE_CSHARP);

fprintf('\n================ SINGLE FRAME ================\n');
fprintf('Input index: %d, angle: %.6f deg\n', test_idx, Ain_7943(test_idx));
fprintf('MATLAB 260331 : D=%+.6f, RL=%.6f, RS=%.6f\n', mD, mRL, mRS);
fprintf('C# equivalent : D=%+.6f, RL=%.6f, RS=%.6f\n', cD, cRL, cRS);
fprintf('C# direct     :            RL=%.6f, RS=%.6f\n', ...
    cDetail.R_L_direct, cDetail.R_S_direct);
fprintf('C# K result   : KFlat=%.6f D, KSteep=%.6f D, Cyl=%.6f D\n', ...
    cDetail.KFlat, cDetail.KSteep, cDetail.Cyl);
fprintf('C# - MATLAB   : dD=%+.6f, dRL=%+.6f, dRS=%+.6f\n', ...
    cD-mD, cRL-mRL, cRS-mRS);


function cal = calibrate_matlab260331(R_true, distPx, L_px, S_px, D_array, pixelSize)
    dL_mm = 2 .* L_px .* pixelSize;
    dS_mm = 2 .* S_px .* pixelSize;
    f_mm = distPx .* pixelSize;

    pL = polyfit(D_array, dL_mm, 1);
    pS = polyfit(D_array, dS_mm, 1);
    pf = polyfit(D_array, f_mm, 1);

    cal.R = R_true;
    cal.f0 = pf(2);
    cal.d0_L = pL(2);
    cal.d0_S = pS(2);
    cal.k_L = pL(1);
    cal.k_S = pS(1);
    cal.Cf_L = cal.d0_L / cal.f0;
    cal.Cf_S = cal.d0_S / cal.f0;
    cal.C_L = cal.d0_L / R_true;
    cal.C_S = cal.d0_S / R_true;
end


function cal = calibrate_csharp260331(R_true, distPx, L_px, S_px, D_array, pixelSize)
    dL_mm = 2 .* L_px .* pixelSize;
    dS_mm = 2 .* S_px .* pixelSize;
    f_mm = distPx .* pixelSize;

    pf = fit_robust_linear_positive(D_array, f_mm);
    pL = fit_robust_linear_positive(D_array, dL_mm, dS_mm, f_mm);
    pS = fit_robust_linear_positive(D_array, dS_mm, dL_mm, f_mm);

    f0 = ensure_finite_positive(pf(2), 'f0');
    d0_L = ensure_finite_positive(pL(2), 'd0_L');
    d0_S = ensure_finite_positive(pS(2), 'd0_S');
    R_stored = double(single(R_true));

    Cf_L = ensure_finite_positive(d0_L / f0, 'Cf_L');
    Cf_S = ensure_finite_positive(d0_S / f0, 'Cf_S');
    C_L = ensure_finite_positive(d0_L / R_stored, 'C_L');
    C_S = ensure_finite_positive(d0_S / R_stored, 'C_S');

    % CalibrationNode stores these values as float in the C# project.
    cal.R = single(R_stored);
    cal.f0 = single(f0);
    cal.d0_L = single(d0_L);
    cal.d0_S = single(d0_S);
    cal.k_L = single(pL(1));
    cal.k_S = single(pS(1));
    cal.Cf_L = single(Cf_L);
    cal.Cf_S = single(Cf_S);
    cal.C_L = single(C_L);
    cal.C_S = single(C_S);
end


function [R_L, R_S, D] = measure_matlab260331(L_px, S_px, angleDeg, distPx, cal_matrix, pixelSize)
    dL_mm = 2 * L_px * pixelSize;
    dS_mm = 2 * S_px * pixelSize;
    f_mm = distPx * pixelSize;

    f0 = [cal_matrix.f0];
    Cf_L = interp1(f0, [cal_matrix.Cf_L], f_mm, 'linear', 'extrap');
    Cf_S = interp1(f0, [cal_matrix.Cf_S], f_mm, 'linear', 'extrap');
    k_L = interp1(f0, [cal_matrix.k_L], f_mm, 'linear', 'extrap');
    k_S = interp1(f0, [cal_matrix.k_S], f_mm, 'linear', 'extrap');
    C_L = interp1(f0, [cal_matrix.C_L], f_mm, 'linear', 'extrap');
    C_S = interp1(f0, [cal_matrix.C_S], f_mm, 'linear', 'extrap');

    d0_L = Cf_L * f_mm;
    d0_S = Cf_S * f_mm;

    if abs(dL_mm - dS_mm) < 0.005
        theta = 0;
    else
        theta = deg2rad(angleDeg);
    end

    if theta == 0
        d_spot = (dL_mm + dS_mm) / 2;
        d0_spot = (d0_L + d0_S) / 2;
        k_spot = (k_L + k_S) / 2;
    else
        c2 = cos(theta)^2;
        s2 = sin(theta)^2;
        d_spot = 1 / (c2/dL_mm + s2/dS_mm);
        d0_spot = 1 / (c2/d0_L + s2/d0_S);
        k_spot = c2*k_L + s2*k_S;
    end

    D = (d_spot - d0_spot) / k_spot;
    R_L = (dL_mm - k_L*D) / C_L;
    R_S = (dS_mm - k_S*D) / C_S;
end


function [R_L_final, R_S_final, D, detail] = measure_csharp260331( ...
        L_px, S_px, angleDeg, distPx, cal_matrix, pixelSize)
    dL_mm = 2 * L_px * pixelSize;
    dS_mm = 2 * S_px * pixelSize;
    f_mm = distPx * pixelSize;

    [~, order] = sort(double([cal_matrix.f0]));
    nodes = cal_matrix(order);
    f0 = double([nodes.f0]);

    Cf_L = interp1_csharp(f0, double([nodes.Cf_L]), f_mm);
    Cf_S = interp1_csharp(f0, double([nodes.Cf_S]), f_mm);
    k_L = interp1_csharp(f0, double([nodes.k_L]), f_mm);
    k_S = interp1_csharp(f0, double([nodes.k_S]), f_mm);
    C_L = interp1_csharp(f0, double([nodes.C_L]), f_mm);
    C_S = interp1_csharp(f0, double([nodes.C_S]), f_mm);

    d0_L = Cf_L * f_mm;
    d0_S = Cf_S * f_mm;

    isSpherical = abs(dL_mm - dS_mm) < 0.005;
    if isSpherical
        theta = 0;
    else
        theta = angleDeg * (pi / 180.0);
    end
    c2 = cos(theta)^2;
    s2 = sin(theta)^2;

    if isSpherical
        d_spot = (dL_mm + dS_mm) / 2.0;
        d0_spot = (d0_L + d0_S) / 2.0;
        k_spot = (k_L + k_S) / 2.0;
    else
        d_spot = 1.0 / (c2/dL_mm + s2/dS_mm);
        d0_spot = 1.0 / (c2/d0_L + s2/d0_S);
        k_spot = c2*k_L + s2*k_S;
    end

    D = (d_spot - d0_spot) / k_spot;
    R_L_direct = (dL_mm - k_L*D) / C_L;
    R_S_direct = (dS_mm - k_S*D) / C_S;

    if ~isSpherical && dS_mm > 1e-9 && dL_mm > 1e-9
        R_spot = interp1_csharp(f0, double([nodes.R]), f_mm);
        r = dL_mm / dS_mm;

        R_L_euler = R_spot * (c2 + r*s2);
        R_S_euler = R_spot * (c2/r + s2);

        R_L_pass1 = c2*R_L_direct + s2*R_L_euler;
        R_S_pass1 = s2*R_S_direct + c2*R_S_euler;

        R_L_euler2 = R_S_pass1 * r;
        R_S_euler2 = R_L_pass1 / r;

        R_L_final = c2*R_L_direct + s2*R_L_euler2;
        R_S_final = s2*R_S_direct + c2*R_S_euler2;
    else
        R_L_final = R_L_direct;
        R_S_final = R_S_direct;
    end

    k1 = 337.5 / R_L_final;
    k2 = 337.5 / R_S_final;
    detail.R_L_direct = R_L_direct;
    detail.R_S_direct = R_S_direct;
    detail.KFlat = min(k1, k2);
    detail.KSteep = max(k1, k2);
    detail.Cyl = abs(k1-k2);
    detail.isSpherical = isSpherical;
end


function batch_compare(R_target, L_px, S_px, angleDeg, distPx, matCal, csCal, ...
        matPixelSize, csPixelSize, D_array)
    fprintf('\nTarget R = %.3f mm\n', R_target);
    fprintf([' D_true |  D_mat   D_cs    dD   |  RL_mat  RL_cs   dRL  ' ...
             '|  RS_mat  RS_cs   dRS\n']);
    fprintf('%s\n', repmat('-', 1, 91));

    maxDelta = zeros(1,3);
    for i = 1:numel(D_array)
        [mRL, mRS, mD] = measure_matlab260331( ...
            L_px(i), S_px(i), angleDeg(i), distPx(i), matCal, matPixelSize);
        [cRL, cRS, cD] = measure_csharp260331( ...
            L_px(i), S_px(i), angleDeg(i), distPx(i), csCal, csPixelSize);

        delta = [cD-mD, cRL-mRL, cRS-mRS];
        maxDelta = max(maxDelta, abs(delta));
        fprintf('%+7.3f | %+7.4f %+7.4f %+7.4f | %7.4f %7.4f %+7.4f | %7.4f %7.4f %+7.4f\n', ...
            D_array(i), mD, cD, delta(1), mRL, cRL, delta(2), mRS, cRS, delta(3));
    end

    fprintf('max abs delta: D=%.6f mm, RL=%.6f mm, RS=%.6f mm\n', ...
        maxDelta(1), maxDelta(2), maxDelta(3));
end


function value = interp1_csharp(x, v, xq)
    x = x(:);
    v = v(:);
    if numel(x) ~= numel(v) || numel(x) < 2
        error('Interpolation arrays must have equal lengths of at least two.');
    end

    n = numel(x);
    if xq <= x(1)
        value = v(1) + (xq-x(1)) * (v(2)-v(1)) / (x(2)-x(1));
        return;
    end
    if xq >= x(n)
        value = v(n) + (xq-x(n)) * (v(n)-v(n-1)) / (x(n)-x(n-1));
        return;
    end

    for i = 1:n-1
        if xq >= x(i) && xq <= x(i+1)
            value = v(i) + (xq-x(i)) * (v(i+1)-v(i)) / (x(i+1)-x(i));
            return;
        end
    end
    value = v(1);
end


function p = fit_robust_linear_positive(x, primary, varargin)
    count = min(numel(x), numel(primary));
    for i = 1:numel(varargin)
        count = min(count, numel(varargin{i}));
    end

    x = x(1:count);
    primary = primary(1:count);
    positive = isfinite(x) & isfinite(primary) & primary > 0;
    finiteMask = isfinite(x) & isfinite(primary);
    for i = 1:numel(varargin)
        required = varargin{i}(1:count);
        positive = positive & isfinite(required) & required > 0;
        finiteMask = finiteMask & isfinite(required);
    end

    if nnz(positive) >= 2
        p = robust_linear_fit_csharp(x(positive), primary(positive), 1.5);
        return;
    end
    if nnz(finiteMask) < 2
        error('Not enough valid points to complete C#-equivalent calibration.');
    end
    p = robust_linear_fit_csharp(x(finiteMask), primary(finiteMask), 1.5);
end


function p = robust_linear_fit_csharp(x, y, sigmaThreshold)
    x = x(:);
    y = y(:);
    initial = linear_fit_csharp(x, y);
    n = numel(x);
    if n <= 2
        p = initial;
        return;
    end

    predicted = initial(1).*x + initial(2);
    errors = abs(y-predicted);
    meanError = sum(errors) / n;
    stdDev = sqrt(sum((errors-meanError).^2) / n);
    if stdDev < 1e-6
        p = initial;
        return;
    end

    keep = errors <= meanError + sigmaThreshold*stdDev;
    if nnz(keep) >= 2 && nnz(keep) < n
        p = linear_fit_csharp(x(keep), y(keep));
    else
        p = initial;
    end
end


function p = linear_fit_csharp(x, y)
    % Exact degree-one form of FunctionMethod.PolynomialFit.
    m = 2;
    matrix = zeros(m, m+1);
    for i = 0:m-1
        for j = 0:m-1
            matrix(i+1,j+1) = sum(x.^(i+j));
        end
        matrix(i+1,m+1) = sum(y .* x.^i);
    end

    for i = 1:m-1
        for k = i+1:m
            t = matrix(k,i) / matrix(i,i);
            for j = 1:m+1
                matrix(k,j) = matrix(k,j) - t*matrix(i,j);
            end
        end
    end

    result = zeros(m,1);
    for i = m:-1:1
        sumKnown = 0;
        for j = i+1:m
            sumKnown = sumKnown + matrix(i,j)*result(j);
        end
        result(i) = (matrix(i,m+1)-sumKnown) / matrix(i,i);
    end
    p = flipud(result).';
end


function value = ensure_finite_positive(value, name)
    if ~isfinite(value) || value <= 0
        error('%s is invalid (value=%g); C# calibration would reject this standard eye.', name, value);
    end
end


function [Dist_centroids, L_inner, S_inner, Ang_inner, L_outer, S_outer, Ang_outer] = ...
        ProcessCorneaImages(imageDir, num_images)
    Dist_centroids = zeros(num_images,1);
    L_inner = zeros(num_images,1);
    S_inner = zeros(num_images,1);
    Ang_inner = zeros(num_images,1);
    L_outer = zeros(num_images,1);
    S_outer = zeros(num_images,1);
    Ang_outer = zeros(num_images,1);

    for i = 1:num_images
        fullPath = fullfile(imageDir, [num2str(i-1), '.png']);
        if ~isfile(fullPath)
            continue;
        end

        img = imread(fullPath);
        if size(img,3) == 3
            img_gray = rgb2gray(img);
        else
            img_gray = img;
        end
        Img_norm = im2double(img_gray);

        [Img_mark, level] = maskCircleFromCenter(Img_norm, 130);
        imgBW = Img_mark < level;
        mask_body = imopen(imgBW, strel('disk',3));
        mask_whole_disk = imfill(mask_body,'holes');
        Img_mark2 = mask_whole_disk .* Img_mark;
        imgBW2 = Img_mark2 > graythresh(Img_mark2(Img_mark2>0));

        [distPx, mask1, mask2] = twoLargestCCDistance(imgBW2, 8, 'centroid');
        Dist_centroids(i,1) = distPx;

        Light_Spot = mask1 + mask2;
        Circular_Spot = imgBW2 - Light_Spot;
        Circular_Spot(Circular_Spot < 0) = 0;
        imgProcessed = imopen(Circular_Spot, strel('disk',2));

        CC = bwconncomp(imgProcessed);
        stats = regionprops(CC,'Area','Centroid','Eccentricity');
        valid_idx = find([stats.Area]>15 & [stats.Area]<200 & [stats.Eccentricity]<0.8);
        if numel(valid_idx) < 6
            continue;
        end

        centroids = vertcat(stats(valid_idx).Centroid);
        x = centroids(:,1);
        y = centroids(:,2);
        try
            global_ellipse = fit_ellipse_direct(x, y);
            centerX = global_ellipse.X0;
            centerY = global_ellipse.Y0;
        catch
            centerX = mean(x);
            centerY = mean(y);
        end

        dists = hypot(x-centerX, y-centerY);
        dists_norm = (dists-min(dists)) / (max(dists)-min(dists));
        level_dist = graythresh(dists_norm);
        split_radius = min(dists) + level_dist*(max(dists)-min(dists));

        innerIdx = dists <= split_radius;
        outerIdx = dists > split_radius;
        XY_inner = [x(innerIdx), y(innerIdx)];
        XY_outer = [x(outerIdx), y(outerIdx)];

        if nnz(innerIdx) >= 6
            e = fit_ellipse_direct(XY_inner(:,1), XY_inner(:,2));
            L_inner(i,1) = e.a;
            S_inner(i,1) = e.b;
            Ang_inner(i,1) = mod(rad2deg(e.phi), 180);
        end
        if nnz(outerIdx) >= 6
            e = fit_ellipse_direct(XY_outer(:,1), XY_outer(:,2));
            L_outer(i,1) = e.a;
            S_outer(i,1) = e.b;
            Ang_outer(i,1) = mod(rad2deg(e.phi), 180);
        end
    end
end


function [I_out, level] = maskCircleFromCenter(I_in, r)
    [H, W, C] = size(I_in);
    cx = (W+1)/2;
    cy = (H+1)/2;
    [X, Y] = meshgrid(1:W, 1:H);
    mask = (X-cx).^2 + (Y-cy).^2 <= r^2;
    I_out = I_in;
    for k = 1:C
        tmp = I_out(:,:,k);
        tmp(~mask) = 1;
        I_out(:,:,k) = tmp;
    end
    if C > 1
        I_gray = rgb2grayLocal(I_in);
    else
        I_gray = I_in;
    end
    roiPixels = im2double(I_gray(mask));
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
    if nargin < 2 || isempty(conn)
        conn = 8;
    end
    if nargin < 3 || isempty(mode)
        mode = 'min';
    end
    BW = logical(BW);
    [H, W] = size(BW);
    centerRow = 241;
    centerCol = 320;
    halfH = 8;
    halfW = 130;
    r1 = max(1,centerRow-halfH);
    r2 = min(H,centerRow+halfH);
    c1 = max(1,centerCol-halfW);
    c2 = min(W,centerCol+halfW);
    BWroi = BW(r1:r2, c1:c2);
    CC = bwconncomp(BWroi, conn);
    distPx = NaN;
    mask1 = false(H,W);
    mask2 = false(H,W);
    if CC.NumObjects < 2
        return;
    end

    stats = regionprops(CC, 'Centroid');
    centroids_x = arrayfun(@(s) s.Centroid(1), stats);
    [~, idLeft] = min(centroids_x);
    [~, idRight] = max(centroids_x);
    if idLeft == idRight
        return;
    end

    m1_roi = false(size(BWroi));
    m2_roi = false(size(BWroi));
    m1_roi(CC.PixelIdxList{idLeft}) = true;
    m2_roi(CC.PixelIdxList{idRight}) = true;
    mask1(r1:r2,c1:c2) = m1_roi;
    mask2(r1:r2,c1:c2) = m2_roi;

    switch lower(mode)
        case 'min'
            D_bw = bwdist(mask1);
            distPx = min(D_bw(mask2));
        case 'centroid'
            left = stats(idLeft).Centroid;
            right = stats(idRight).Centroid;
            leftFull = [left(1)+(c1-1), left(2)+(r1-1)];
            rightFull = [right(1)+(c1-1), right(2)+(r1-1)];
            distPx = hypot(leftFull(1)-rightFull(1), leftFull(2)-rightFull(2));
    end
end


function ellipse_t = fit_ellipse_direct(x, y)
    x = x(:);
    y = y(:);
    if numel(x) < 6
        error('At least 6 points required');
    end
    mean_x = mean(x);
    mean_y = mean(y);
    sx = (max(x)-min(x))/2;
    sy = (max(y)-min(y))/2;
    smax = max(sx,sy);
    if smax == 0
        smax = 1;
    end
    xx = (x-mean_x)/smax;
    yy = (y-mean_y)/smax;
    D_mat = [xx.^2, xx.*yy, yy.^2, xx, yy, ones(size(xx))];
    S_mat = D_mat'*D_mat;
    C_mat = zeros(6,6);
    C_mat(1,3) = 2;
    C_mat(3,1) = 2;
    C_mat(2,2) = -1;
    [gevec,~] = eig(S_mat,C_mat);
    gevec = real(gevec);
    cond_vals = 4*gevec(1,:).*gevec(3,:)-gevec(2,:).^2;
    pos_idx = find(cond_vals>1e-10);
    if isempty(pos_idx)
        error('Fit failed: Non-ellipse');
    end
    [~,max_idx] = max(cond_vals(pos_idx));
    v = gevec(:,pos_idx(max_idx));
    v = v/sqrt(cond_vals(pos_idx(max_idx)));
    A = v(1)/smax^2;
    B = v(2)/smax^2;
    Cv = v(3)/smax^2;
    Dc = v(4)/smax-2*A*mean_x-B*mean_y;
    Ec = v(5)/smax-2*Cv*mean_y-B*mean_x;
    Fc = v(6)+A*mean_x^2+B*mean_x*mean_y+Cv*mean_y^2 ...
        -v(4)*mean_x/smax-v(5)*mean_y/smax;
    delta = B^2-4*A*Cv;
    X0 = (2*Cv*Dc-B*Ec)/delta;
    Y0 = (2*A*Ec-B*Dc)/delta;
    Fp = A*X0^2+B*X0*Y0+Cv*Y0^2+Dc*X0+Ec*Y0+Fc;
    Ap = -A/Fp;
    Bp = -B/Fp;
    Cp = -Cv/Fp;
    trM = Ap+Cp;
    detM = Ap*Cp-(Bp/2)^2;
    if detM <= 0
        error('Fit failed');
    end
    diffM = sqrt((Ap-Cp)^2+Bp^2);
    l1 = (trM+diffM)/2;
    l2 = (trM-diffM)/2;
    if l1 <= 0 || l2 <= 0
        error('Fit failed');
    end
    ellipse_t.X0 = X0;
    ellipse_t.Y0 = Y0;
    ellipse_t.a = 1/sqrt(l2);
    ellipse_t.b = 1/sqrt(l1);
    ellipse_t.phi = atan2(l2-Ap, Bp/2);
end

